import importlib.util
import json
import os
import sys
import types
import unittest
from pathlib import Path


class _KeyExpression:
    def eq(self, value):
        return self

    def begins_with(self, value):
        return self

    def __and__(self, other):
        return self


class _FakeTable:
    def __init__(self):
        self.results = []
        self.calls = []

    def query(self, **kwargs):
        self.calls.append(kwargs)
        return self.results.pop(0)


def _load_backend(fake_table):
    boto3 = types.ModuleType("boto3")
    boto3.resource = lambda service: types.SimpleNamespace(Table=lambda name: fake_table)
    conditions = types.ModuleType("boto3.dynamodb.conditions")
    conditions.Key = lambda name: _KeyExpression()
    dynamodb = types.ModuleType("boto3.dynamodb")
    dynamodb.conditions = conditions
    botocore = types.ModuleType("botocore")
    exceptions = types.ModuleType("botocore.exceptions")
    exceptions.ClientError = type("ClientError", (Exception,), {})
    botocore.exceptions = exceptions
    configuration = types.ModuleType("configuration")
    configuration.load_chzzk_configuration = lambda: None

    sys.modules.update({
        "boto3": boto3,
        "boto3.dynamodb": dynamodb,
        "boto3.dynamodb.conditions": conditions,
        "botocore": botocore,
        "botocore.exceptions": exceptions,
        "configuration": configuration,
    })
    os.environ.update({
        "TABLE_NAME": "test-table",
        "TOKEN_SIGNING_SECRET": "test-signing-secret",
        "FRONTEND_URL": "https://example.invalid",
    })
    path = Path(__file__).parents[2] / "aws" / "backend" / "app.py"
    spec = importlib.util.spec_from_file_location("cotl_backend_test_subject", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FollowerStateRecoveryApiTests(unittest.TestCase):
    def setUp(self):
        self.table = _FakeTable()
        self.app = _load_backend(self.table)

    def test_paged_state_query_derives_identity_from_sort_key(self):
        self.table.results.append({
            "Items": [
                {
                    "SK": "STATE#slot_0#trusted-viewer",
                    "Payload": json.dumps({"saveId": "wrong", "viewerChannelId": "untrusted", "revision": 3, "history": []}),
                },
                {"SK": "STATE#slot_0#corrupt", "Payload": "{"},
                {"SK": "STATE#slot_0#not-an-object", "Payload": "[]"},
            ],
            "LastEvaluatedKey": {"PK": "STREAMER#streamer", "SK": "STATE#slot_0#trusted-viewer"},
        })
        states, cursor = self.app.get_follower_states("streamer", "slot_0")

        self.assertEqual(1, len(states))
        self.assertEqual("slot_0", states[0]["saveId"])
        self.assertEqual("trusted-viewer", states[0]["viewerChannelId"])
        self.assertTrue(cursor)

        self.table.results.append({"Items": [], "LastEvaluatedKey": None})
        next_states, next_cursor = self.app.get_follower_states("streamer", "slot_0", cursor)
        self.assertEqual([], next_states)
        self.assertIsNone(next_cursor)
        self.assertEqual(
            {"PK": "STREAMER#streamer", "SK": "STATE#slot_0#trusted-viewer"},
            self.table.calls[-1]["ExclusiveStartKey"],
        )

    def test_cursor_cannot_cross_save_boundary(self):
        cursor = self.app._b64u(b"STATE#slot_1#viewer")
        with self.assertRaisesRegex(ValueError, "does not match saveId"):
            self.app.get_follower_states("streamer", "slot_0", cursor)

    def test_route_requires_matching_companion_streamer(self):
        self.table.results.append({
            "Items": [{"SK": "STATE#slot_0#viewer", "Payload": json.dumps({"revision": 1, "history": []})}],
            "LastEvaluatedKey": None,
        })
        token = self.app.sign_token({"role": "companion", "sub": "streamer"}, 60)
        event = {
            "rawPath": "/streamers/streamer/follower-states",
            "queryStringParameters": {"saveId": "slot_0"},
            "headers": {"authorization": "Bearer " + token},
            "requestContext": {"http": {"method": "GET"}},
        }
        response = self.app.handler(event, None)
        self.assertEqual(200, response["statusCode"])
        self.assertEqual("viewer", json.loads(response["body"])["states"][0]["viewerChannelId"])

        wrong_token = self.app.sign_token({"role": "companion", "sub": "other-streamer"}, 60)
        event["headers"]["authorization"] = "Bearer " + wrong_token
        response = self.app.handler(event, None)
        self.assertEqual(401, response["statusCode"])


if __name__ == "__main__":
    unittest.main()
