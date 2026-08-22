import base64
import hashlib
import hmac
import json
import os
import time
import urllib.parse
import urllib.request
from typing import Any

import boto3
from configuration import load_chzzk_configuration

TABLE = os.environ["TABLE_NAME"]
CONFIG = load_chzzk_configuration()
MYLAMB_CHZZK_CLIENT_ID = CONFIG.mylamb.client_id
MYLAMB_CHZZK_CLIENT_SECRET = CONFIG.mylamb.client_secret
TOKEN_SECRET = os.environ["TOKEN_SIGNING_SECRET"].encode("utf-8")
FRONTEND_URL = os.environ["FRONTEND_URL"].rstrip("/")
CHZZK_BASE = "https://openapi.chzzk.naver.com"
CHZZK_AUTH = "https://chzzk.naver.com/account-interlock"

ddb = boto3.resource("dynamodb").Table(TABLE)


def _json(status: int, body: Any, headers: dict[str, str] | None = None):
    out_headers = {
        "content-type": "application/json; charset=utf-8",
        "access-control-allow-origin": FRONTEND_URL,
        "access-control-allow-headers": "authorization,content-type",
        "access-control-allow-methods": "GET,PUT,POST,OPTIONS",
        "cache-control": "no-store",
    }
    if headers:
        out_headers.update(headers)
    return {"statusCode": status, "headers": out_headers, "body": json.dumps(body, ensure_ascii=False)}


def _redirect(url: str):
    return {"statusCode": 302, "headers": {"location": url, "cache-control": "no-store"}, "body": ""}


def _b64u(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode().rstrip("=")


def _unb64u(text: str) -> bytes:
    return base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))


def sign_token(payload: dict[str, Any], ttl_seconds: int) -> str:
    data = dict(payload)
    data["exp"] = int(time.time()) + ttl_seconds
    encoded = _b64u(json.dumps(data, separators=(",", ":"), ensure_ascii=False).encode())
    sig = _b64u(hmac.new(TOKEN_SECRET, encoded.encode(), hashlib.sha256).digest())
    return f"{encoded}.{sig}"


def verify_token(token: str, required_role: str | None = None) -> dict[str, Any]:
    try:
        encoded, signature = token.split(".", 1)
        expected = _b64u(hmac.new(TOKEN_SECRET, encoded.encode(), hashlib.sha256).digest())
        if not hmac.compare_digest(signature, expected):
            raise ValueError("bad signature")
        payload = json.loads(_unb64u(encoded))
        if int(payload.get("exp", 0)) <= int(time.time()):
            raise ValueError("expired")
        if required_role and payload.get("role") != required_role:
            raise ValueError("wrong role")
        return payload
    except Exception as exc:
        raise PermissionError("invalid session token") from exc


def bearer(event) -> str:
    value = (event.get("headers") or {}).get("authorization") or (event.get("headers") or {}).get("Authorization") or ""
    if not value.lower().startswith("bearer "):
        raise PermissionError("missing bearer token")
    return value[7:].strip()


def api_public_url(event) -> str:
    """Derive this HTTP API's public origin from the incoming request.

    Do not reference the SAM HttpApi resource from the Lambda environment: the
    generated HttpApi integration already depends on this function, and doing
    so creates a CloudFormation circular dependency.
    """
    headers = event.get("headers") or {}
    request_context = event.get("requestContext") or {}
    domain = request_context.get("domainName") or headers.get("host") or headers.get("Host")
    if not domain:
        raise RuntimeError("unable to determine API public domain from request")
    scheme = headers.get("x-forwarded-proto") or headers.get("X-Forwarded-Proto") or "https"
    return f"{scheme}://{domain}".rstrip("/")


def http_json(method: str, url: str, body: dict[str, Any] | None = None, token: str | None = None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("accept", "application/json")
    if data is not None:
        req.add_header("content-type", "application/json")
    if token:
        req.add_header("authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=10) as response:
        return json.loads(response.read().decode())


def chzzk_me(access_token: str) -> dict[str, Any]:
    envelope = http_json("GET", CHZZK_BASE + "/open/v1/users/me", token=access_token)
    content = envelope.get("content")
    if not content:
        raise RuntimeError(envelope.get("message") or "CHZZK /users/me returned no content")
    return content


def exchange_code(code: str, state: str) -> str:
    envelope = http_json("POST", CHZZK_BASE + "/auth/v1/token", {
        "grantType": "authorization_code",
        "clientId": MYLAMB_CHZZK_CLIENT_ID,
        "clientSecret": MYLAMB_CHZZK_CLIENT_SECRET,
        "code": code,
        "state": state,
    })
    content = envelope.get("content")
    if not content or not content.get("accessToken"):
        raise RuntimeError(envelope.get("message") or "CHZZK token exchange failed")
    return content["accessToken"]


def get_catalog(streamer: str):
    item = ddb.get_item(Key={"PK": f"STREAMER#{streamer}", "SK": "CATALOG"}).get("Item")
    if not item:
        return None
    return json.loads(item["Payload"])


def put_catalog(streamer: str, payload: dict[str, Any]):
    ddb.put_item(Item={
        "PK": f"STREAMER#{streamer}",
        "SK": "CATALOG",
        "Payload": json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
        "UpdatedAt": int(time.time()),
    })


def validate_appearance(catalog: dict[str, Any], appearance: dict[str, Any]):
    form_id = appearance.get("formId")
    allowed = set(catalog.get("allowedFormIds") or [])
    if not form_id or form_id not in allowed:
        raise ValueError("form is not allowed by this streamer")
    form = next((x for x in catalog.get("forms", []) if x.get("formId") == form_id), None)
    if not form:
        raise ValueError("form does not exist in the current streamer catalog")
    variant = appearance.get("variantId")
    color = appearance.get("colorId")
    variants = form.get("variantIds") or []
    colors = form.get("colorIds") or []
    if variant is not None and variants and variant not in variants:
        raise ValueError("variant is not valid")
    if color is not None and colors and color not in colors:
        raise ValueError("color is not valid")


def handler(event, context):
    method = (event.get("requestContext") or {}).get("http", {}).get("method", "GET")
    path = event.get("rawPath") or "/"
    qs = event.get("queryStringParameters") or {}

    if method == "OPTIONS":
        return _json(204, {})

    try:
        if path == "/health":
            return _json(200, {"ok": True, "service": "cotl-companion-api"})

        if path == "/auth/chzzk/start" and method == "GET":
            streamer = qs.get("streamer", "")
            if not streamer:
                return _json(400, {"error": "streamer is required"})
            state = sign_token({"role": "oauth-state", "streamer": streamer}, 600)
            redirect_uri = api_public_url(event) + "/auth/chzzk/callback"
            url = CHZZK_AUTH + "?" + urllib.parse.urlencode({
                "clientId": MYLAMB_CHZZK_CLIENT_ID,
                "redirectUri": redirect_uri,
                "state": state,
            })
            return _redirect(url)

        if path == "/auth/chzzk/callback" and method == "GET":
            code = qs.get("code", "")
            state = qs.get("state", "")
            oauth_state = verify_token(state, "oauth-state")
            access_token = exchange_code(code, state)
            me = chzzk_me(access_token)
            viewer_token = sign_token({
                "role": "viewer",
                "sub": me["channelId"],
                "nickname": me.get("channelName", ""),
            }, 12 * 60 * 60)
            target = FRONTEND_URL + "/?" + urllib.parse.urlencode({"streamer": oauth_state["streamer"]}) + "#token=" + urllib.parse.quote(viewer_token)
            return _redirect(target)

        if path == "/companion/session" and method == "POST":
            me = chzzk_me(bearer(event))
            token = sign_token({"role": "companion", "sub": me["channelId"], "nickname": me.get("channelName", "")}, 60 * 60)
            return _json(200, {"token": token, "streamerChannelId": me["channelId"]})

        parts = [x for x in path.split("/") if x]
        if len(parts) >= 3 and parts[0] == "streamers":
            streamer = parts[1]

            if parts[2] == "catalog":
                if method == "GET":
                    catalog = get_catalog(streamer)
                    return _json(404, {"error": "catalog not available"}) if catalog is None else _json(200, catalog)
                if method == "PUT":
                    session = verify_token(bearer(event), "companion")
                    if session["sub"] != streamer:
                        raise PermissionError("streamer mismatch")
                    body = json.loads(event.get("body") or "{}")
                    save_id = body.get("saveId") or "unknown"
                    forms = body.get("forms") or []
                    allowed = body.get("allowedFormIds") or []
                    if save_id == "unknown" or len(forms) == 0:
                        return _json(409, {
                            "ok": False,
                            "error": "transient catalog",
                            "saveId": save_id,
                            "forms": len(forms),
                            "allowed": len(allowed),
                        })
                    put_catalog(streamer, body)
                    print(f"catalog stored+verified streamer={streamer} save={save_id} forms={len(forms)} allowed={len(allowed)}")
                    return _json(200, {
                        "ok": True,
                        "saveId": save_id,
                        "forms": len(forms),
                        "allowed": len(allowed),
                    })

            if len(parts) == 4 and parts[2] == "appearance" and parts[3] == "me":
                session = verify_token(bearer(event), "viewer")
                key = {"PK": f"STREAMER#{streamer}", "SK": f"VIEWER#{session['sub']}"}
                if method == "GET":
                    item = ddb.get_item(Key=key).get("Item")
                    if not item:
                        return _json(404, {"error": "appearance not set"})
                    return _json(200, json.loads(item["Payload"]))
                if method == "PUT":
                    catalog = get_catalog(streamer)
                    if not catalog:
                        return _json(409, {"error": "streamer catalog is not available yet"})
                    body = json.loads(event.get("body") or "{}")
                    appearance = body.get("appearance") or body
                    validate_appearance(catalog, appearance)
                    payload = {
                        "viewerChannelId": session["sub"],
                        "viewerNickname": session.get("nickname", ""),
                        "appearance": appearance,
                        "updatedAt": int(time.time()),
                    }
                    ddb.put_item(Item={"PK": key["PK"], "SK": key["SK"], "Payload": json.dumps(payload, ensure_ascii=False, separators=(",", ":")), "UpdatedAt": int(time.time())})
                    return _json(200, payload)

            if len(parts) == 6 and parts[2] == "viewers" and parts[4] == "appearance":
                # /streamers/{streamer}/viewers/{viewer}/appearance
                pass
            if len(parts) == 5 and parts[2] == "viewers" and parts[4] == "appearance" and method == "GET":
                viewer = parts[3]
                session = verify_token(bearer(event), "companion")
                if session["sub"] != streamer:
                    raise PermissionError("streamer mismatch")
                item = ddb.get_item(Key={"PK": f"STREAMER#{streamer}", "SK": f"VIEWER#{viewer}"}).get("Item")
                if not item:
                    return _json(404, {"error": "appearance not set"})
                return _json(200, json.loads(item["Payload"]))

        return _json(404, {"error": "not found", "path": path})

    except PermissionError as exc:
        return _json(401, {"error": str(exc)})
    except ValueError as exc:
        return _json(400, {"error": str(exc)})
    except Exception as exc:
        print("ERROR", repr(exc))
        return _json(500, {"error": "internal error"})
