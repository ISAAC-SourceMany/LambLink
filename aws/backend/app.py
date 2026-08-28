import base64
import hashlib
import hmac
import json
import os
import time
import secrets
import urllib.parse
import urllib.request
from typing import Any

import boto3
from boto3.dynamodb.conditions import Key
from botocore.exceptions import ClientError
from configuration import load_chzzk_configuration

TABLE = os.environ["TABLE_NAME"]
TOKEN_SECRET = os.environ["TOKEN_SIGNING_SECRET"].encode("utf-8")
FRONTEND_URL = os.environ["FRONTEND_URL"].rstrip("/")
CHZZK_BASE = "https://openapi.chzzk.naver.com"
CHZZK_AUTH = "https://chzzk.naver.com/account-interlock"
CONFIG = None

ddb = boto3.resource("dynamodb").Table(TABLE)


def chzzk_configuration():
    """Load CHZZK secrets only for OAuth/session routes.

    This keeps the health check and public catalog available while a new staging
    stack is waiting for its CHZZK applications to be registered.
    """
    global CONFIG
    if CONFIG is None:
        CONFIG = load_chzzk_configuration()
    return CONFIG


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


def exchange_code_with_credentials(code: str, state: str, client_id: str, client_secret: str) -> dict[str, Any]:
    envelope = http_json("POST", CHZZK_BASE + "/auth/v1/token", {
        "grantType": "authorization_code",
        "clientId": client_id,
        "clientSecret": client_secret,
        "code": code,
        "state": state,
    })
    content = envelope.get("content")
    if not content or not content.get("accessToken"):
        raise RuntimeError(envelope.get("message") or "CHZZK token exchange failed")
    return content


def exchange_code(code: str, state: str) -> str:
    credentials = chzzk_configuration().mylamb
    return exchange_code_with_credentials(
        code, state, credentials.client_id, credentials.client_secret
    )["accessToken"]


def refresh_token(refresh_token_value: str) -> dict[str, Any]:
    credentials = chzzk_configuration().companion
    envelope = http_json("POST", CHZZK_BASE + "/auth/v1/token", {
        "grantType": "refresh_token",
        "refreshToken": refresh_token_value,
        "clientId": credentials.client_id,
        "clientSecret": credentials.client_secret,
    })
    return envelope.get("content") or {}


def _validate_companion_callback(value: str) -> str:
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost"):
        raise ValueError("invalid companion callback")
    if parsed.port != 17881 or parsed.path != "/callback/":
        raise ValueError("invalid companion callback")
    if parsed.query or parsed.fragment:
        raise ValueError("invalid companion callback")
    return value


def create_companion_ticket(token_content: dict[str, Any], me: dict[str, Any], nonce: str) -> str:
    ticket = secrets.token_urlsafe(32)
    expires_at = int(time.time()) + 120
    ddb.put_item(Item={
        "PK": f"AUTH#{ticket}",
        "SK": "COMPANION",
        "Payload": json.dumps({
            "accessToken": token_content.get("accessToken", ""),
            "refreshToken": token_content.get("refreshToken", ""),
            "tokenType": token_content.get("tokenType", "Bearer"),
            "expiresIn": token_content.get("expiresIn", 86400),
            "streamerChannelId": me.get("channelId", ""),
            "streamerChannelName": me.get("channelName", ""),
            "nonce": nonce,
        }, ensure_ascii=False),
        "ExpiresAt": expires_at,
    })
    return ticket


def consume_companion_ticket(ticket: str, nonce: str) -> dict[str, Any]:
    key = {"PK": f"AUTH#{ticket}", "SK": "COMPANION"}
    item = ddb.get_item(Key=key, ConsistentRead=True).get("Item")
    if not item:
        raise PermissionError("invalid or already-used companion ticket")
    try:
        if int(item.get("ExpiresAt", 0)) <= int(time.time()):
            raise PermissionError("expired companion ticket")
        payload = json.loads(item.get("Payload") or "{}")
        if not hmac.compare_digest(str(payload.get("nonce", "")), nonce):
            raise PermissionError("companion nonce mismatch")
        payload.pop("nonce", None)
        return payload
    finally:
        ddb.delete_item(Key=key)


def get_catalog(streamer: str):
    item = ddb.get_item(Key={"PK": f"STREAMER#{streamer}", "SK": "CATALOG"}, ConsistentRead=True).get("Item")
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
    if not form.get("isUnlocked", True):
        raise ValueError("form is not unlocked in the current game save")
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

        if path == "/auth/companion/start" and method == "GET":
            credentials = chzzk_configuration().companion
            callback = _validate_companion_callback(qs.get("callback", ""))
            nonce = qs.get("nonce", "")
            if len(nonce) < 16 or len(nonce) > 128:
                return _json(400, {"error": "invalid nonce"})
            state = sign_token({"role": "companion-oauth-state", "callback": callback, "nonce": nonce}, 600)
            redirect_uri = api_public_url(event) + "/auth/companion/callback"
            url = CHZZK_AUTH + "?" + urllib.parse.urlencode({
                "clientId": credentials.client_id,
                "redirectUri": redirect_uri,
                "state": state,
            })
            return _redirect(url)

        if path == "/auth/companion/callback" and method == "GET":
            credentials = chzzk_configuration().companion
            code = qs.get("code", "")
            state = qs.get("state", "")
            oauth_state = verify_token(state, "companion-oauth-state")
            token_content = exchange_code_with_credentials(
                code, state, credentials.client_id, credentials.client_secret
            )
            me = chzzk_me(token_content["accessToken"])
            ticket = create_companion_ticket(token_content, me, str(oauth_state["nonce"]))
            target = str(oauth_state["callback"]) + "?" + urllib.parse.urlencode({
                "ticket": ticket,
                "nonce": oauth_state["nonce"],
            })
            return _redirect(target)

        if path == "/auth/companion/token" and method == "POST":
            body = json.loads(event.get("body") or "{}")
            ticket = str(body.get("ticket") or "")
            nonce = str(body.get("nonce") or "")
            if not ticket or not nonce:
                return _json(400, {"error": "ticket and nonce are required"})
            return _json(200, consume_companion_ticket(ticket, nonce))

        if path == "/auth/companion/refresh" and method == "POST":
            body = json.loads(event.get("body") or "{}")
            old_refresh = str(body.get("refreshToken") or "")
            if not old_refresh: return _json(400, {"error": "refreshToken is required"})
            content = refresh_token(old_refresh)
            if not content.get("accessToken") or not content.get("refreshToken"):
                return _json(401, {"error": "CHZZK token refresh failed"})
            me = chzzk_me(content["accessToken"])
            return _json(200, {**content, "streamerChannelId": me.get("channelId", ""), "streamerChannelName": me.get("channelName", "")})

        if path == "/auth/chzzk/start" and method == "GET":
            credentials = chzzk_configuration().mylamb
            streamer = qs.get("streamer", "")
            if not streamer:
                return _json(400, {"error": "streamer is required"})
            state = sign_token({"role": "oauth-state", "streamer": streamer}, 600)
            redirect_uri = api_public_url(event) + "/auth/chzzk/callback"
            url = CHZZK_AUTH + "?" + urllib.parse.urlencode({
                "clientId": credentials.client_id,
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
                save_id = (get_catalog(streamer) or {}).get("saveId") or "unknown"
                key = {"PK": f"STREAMER#{streamer}", "SK": f"DRAFT#{save_id}#{session['sub']}"}
                state_key = {"PK": f"STREAMER#{streamer}", "SK": f"STATE#{save_id}#{session['sub']}"}
                if method == "GET":
                    item = ddb.get_item(Key=key, ConsistentRead=True).get("Item")
                    state_item = ddb.get_item(Key=state_key, ConsistentRead=True).get("Item")
                    state = json.loads(state_item["Payload"]) if state_item else {"canCreate": True, "nextGeneration": 1, "history": []}
                    draft = json.loads(item["Payload"]) if item else {}
                    return _json(200, {**state, **draft, "saveId": save_id})
                if method == "PUT":
                    catalog = get_catalog(streamer)
                    if not catalog:
                        return _json(409, {"error": "streamer catalog is not available yet"})
                    body = json.loads(event.get("body") or "{}")
                    appearance = body.get("appearance") or body
                    validate_appearance(catalog, appearance)
                    state_item = ddb.get_item(Key=state_key, ConsistentRead=True).get("Item")
                    state = json.loads(state_item["Payload"]) if state_item else {"canCreate": True, "nextGeneration": 1}
                    if not state.get("canCreate", True):
                        return _json(409, {"error": "현재 생성 가능한 신도가 없습니다"})
                    current = ddb.get_item(Key=key, ConsistentRead=True).get("Item")
                    current_payload = json.loads(current["Payload"]) if current else {}
                    next_generation = int(state.get("nextGeneration") or 1)
                    if current_payload.get("status") in ("Reserved", "AppliedUnconfirmed", "Created") and int(current_payload.get("generation") or 1) == next_generation:
                        return _json(409, {"error": "래플 당첨 외형이 확정되어 수정할 수 없습니다"})
                    payload = {
                        "viewerChannelId": session["sub"],
                        "viewerNickname": session.get("nickname", ""),
                        "appearance": appearance,
                        "generation": next_generation,
                        "revision": int(current_payload.get("revision") or 0) + 1,
                        "status": "Draft",
                        "updatedAt": int(time.time()),
                    }
                    ddb.put_item(Item={"PK": key["PK"], "SK": key["SK"], "Payload": json.dumps(payload, ensure_ascii=False, separators=(",", ":")), "UpdatedAt": int(time.time())})
                    return _json(200, {**state, **payload, "saveId": save_id})

            if len(parts) == 6 and parts[2] == "viewers" and parts[4] == "appearance":
                # Atomically freeze the exact latest draft used by this raffle winner.
                viewer = parts[3]
                session = verify_token(bearer(event), "companion")
                if session["sub"] != streamer: raise PermissionError("streamer mismatch")
                if parts[5] == "finalize" and method == "POST":
                    body = json.loads(event.get("body") or "{}")
                    save_id = str(body.get("saveId") or "unknown")
                    generation = int(body.get("generation") or 1)
                    key = {"PK": f"STREAMER#{streamer}", "SK": f"DRAFT#{save_id}#{viewer}"}
                    item = ddb.get_item(Key=key, ConsistentRead=True).get("Item")
                    payload = json.loads(item["Payload"]) if item else {
                        "viewerChannelId": viewer, "viewerNickname": str(body.get("viewerNickname") or ""),
                        "appearance": None, "generation": generation, "revision": 0, "status": "Draft", "updatedAt": int(time.time())
                    }
                    if int(payload.get("generation") or 1) != generation:
                        return _json(409, {"error": "appearance generation mismatch"})
                    current_status = str(payload.get("status") or "Draft")
                    if current_status == "Reserved":
                        if payload.get("raffleId") != body.get("raffleId"):
                            return _json(409, {"error": "appearance is reserved by another raffle"})
                    elif current_status != "Draft":
                        return _json(409, {"error": f"appearance cannot be finalized from {current_status}"})
                    else:
                        old_payload = item["Payload"] if item else None
                        payload["status"] = "Reserved"
                        payload["raffleId"] = body.get("raffleId")
                        payload["recruitFollowerId"] = int(body.get("recruitFollowerId") or 0)
                        payload["followerName"] = str(body.get("followerName") or "")
                        payload["reservedAt"] = int(time.time())
                        new_payload = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
                        try:
                            if item:
                                ddb.update_item(Key=key, UpdateExpression="SET Payload=:new, UpdatedAt=:now", ConditionExpression="Payload=:old", ExpressionAttributeValues={":new": new_payload, ":old": old_payload, ":now": int(time.time())})
                            else:
                                ddb.put_item(Item={**key, "Payload": new_payload, "UpdatedAt": int(time.time())}, ConditionExpression="attribute_not_exists(PK)")
                        except ddb.meta.client.exceptions.ConditionalCheckFailedException:
                            return _json(409, {"error": "appearance changed during finalization; retry"})
                    return _json(200, payload)
                if parts[5] == "status" and method == "POST":
                    body = json.loads(event.get("body") or "{}")
                    save_id = str(body.get("saveId") or "unknown")
                    generation = int(body.get("generation") or 1)
                    key = {"PK": f"STREAMER#{streamer}", "SK": f"DRAFT#{save_id}#{viewer}"}
                    item = ddb.get_item(Key=key, ConsistentRead=True).get("Item")
                    if not item: return _json(404, {"error": "reservation not found"})
                    payload = json.loads(item["Payload"])
                    if int(payload.get("generation") or 1) != generation or payload.get("raffleId") != body.get("raffleId"):
                        return _json(409, {"error": "reservation identity mismatch"})
                    requested_status = str(body.get("status") or "AppliedUnconfirmed")
                    current_status = str(payload.get("status") or "Draft")
                    if current_status == "Created" or (requested_status == "AppliedUnconfirmed" and current_status != "Reserved"):
                        return _json(200, payload)
                    old_payload = item["Payload"]
                    payload["status"] = requested_status
                    if body.get("appliedAppearance") is not None: payload["appliedAppearance"] = body["appliedAppearance"]
                    payload["statusUpdatedAt"] = int(time.time())
                    new_payload = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
                    ddb.update_item(Key=key, UpdateExpression="SET Payload=:new, UpdatedAt=:now", ConditionExpression="Payload=:old", ExpressionAttributeValues={":new": new_payload, ":old": old_payload, ":now": int(time.time())})
                    return _json(200, payload)
            if len(parts) == 5 and parts[2] == "viewers" and parts[4] == "appearance" and method == "GET":
                viewer = parts[3]
                session = verify_token(bearer(event), "companion")
                if session["sub"] != streamer:
                    raise PermissionError("streamer mismatch")
                catalog = get_catalog(streamer) or {}
                save_id = catalog.get("saveId") or "unknown"
                item = ddb.get_item(Key={"PK": f"STREAMER#{streamer}", "SK": f"DRAFT#{save_id}#{viewer}"}, ConsistentRead=True).get("Item")
                if not item:
                    return _json(404, {"error": "appearance not set"})
                return _json(200, json.loads(item["Payload"]))

            if len(parts) == 5 and parts[2] == "viewers" and parts[4] == "state" and method == "PUT":
                viewer = parts[3]
                session = verify_token(bearer(event), "companion")
                if session["sub"] != streamer:
                    raise PermissionError("streamer mismatch")
                body = json.loads(event.get("body") or "{}")
                save_id = str(body.get("saveId") or "unknown")
                if save_id == "unknown": return _json(400, {"error": "saveId is required"})
                payload = {**body, "viewerChannelId": viewer, "updatedAt": int(time.time())}
                key = {"PK": f"STREAMER#{streamer}", "SK": f"STATE#{save_id}#{viewer}"}
                revision = int(payload.get("revision") or 0)
                new_payload = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
                try:
                    ddb.update_item(Key=key,
                        UpdateExpression="SET Payload=:new, UpdatedAt=:now, Revision=:revision",
                        ConditionExpression="attribute_not_exists(Revision) OR Revision < :revision",
                        ExpressionAttributeValues={":new": new_payload, ":now": int(time.time()), ":revision": revision})
                except ClientError as exc:
                    if exc.response.get("Error", {}).get("Code") == "ConditionalCheckFailedException":
                        return _json(200, {"ok": True, "staleIgnored": True, "revision": revision})
                    raise
                return _json(200, {"ok": True, "revision": revision})

            if parts[2] == "reservations" and len(parts) == 3 and method == "GET":
                session = verify_token(bearer(event), "companion")
                if session["sub"] != streamer: raise PermissionError("streamer mismatch")
                save_id = str(qs.get("saveId") or "unknown")
                pending = []
                query_args = {"KeyConditionExpression": Key("PK").eq(f"STREAMER#{streamer}") & Key("SK").begins_with(f"DRAFT#{save_id}#"), "ConsistentRead": True}
                while True:
                    result = ddb.query(**query_args)
                    for item in result.get("Items", []):
                        reservation = json.loads(item["Payload"])
                        if reservation.get("status") in ("Reserved", "AppliedUnconfirmed"): pending.append(reservation)
                    if not result.get("LastEvaluatedKey"): break
                    query_args["ExclusiveStartKey"] = result["LastEvaluatedKey"]
                return _json(200, {"reservations": pending})

        return _json(404, {"error": "not found", "path": path})

    except PermissionError as exc:
        return _json(401, {"error": str(exc)})
    except ValueError as exc:
        return _json(400, {"error": str(exc)})
    except Exception as exc:
        print("ERROR", repr(exc))
        return _json(500, {"error": "internal error"})
