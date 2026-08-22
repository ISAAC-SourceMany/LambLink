#!/usr/bin/env python3
"""Local My Lamb backend/frontend for pre-AWS end-to-end testing.

Runs the same API contract used by AppearanceApiClient, but stores data in a local
JSON file instead of DynamoDB. It can use real CHZZK OAuth/API or an explicit
mock login mode for UI/API smoke tests.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import secrets
import threading
import time
import urllib.parse
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

# Reuse the same configuration provider that the AWS Lambda backend will use.
AWS_BACKEND_DIR = Path(__file__).resolve().parent.parent / "aws" / "backend"
import sys
sys.path.insert(0, str(AWS_BACKEND_DIR))
from configuration import load_chzzk_configuration, ConfigurationError

CHZZK_BASE = "https://openapi.chzzk.naver.com"
CHZZK_AUTH = "https://chzzk.naver.com/account-interlock"

ROOT = Path(__file__).resolve().parent.parent
FRONTEND_FILE = ROOT / "aws" / "frontend" / "index.html"
DEFAULT_DATA_FILE = Path(os.environ.get("COTL_LOCAL_DATA_FILE", ROOT / "local-server" / "data.json"))
DEFAULT_SECRET_FILE = Path(os.environ.get("COTL_LOCAL_TOKEN_SECRET_FILE", ROOT / "local-server" / ".token-secret"))
DEFAULT_PREVIEW_ROOT = Path(os.environ.get("COTL_LOCAL_PREVIEW_ROOT", ROOT / "local-server" / "preview-cache"))


def b64u(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode().rstrip("=")


def unb64u(text: str) -> bytes:
    return base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))


class Store:
    def __init__(self, path: Path, preview_root: Path = DEFAULT_PREVIEW_ROOT):
        self.path = path
        self.preview_root = preview_root
        self.lock = threading.RLock()
        self.data: dict[str, Any] = {"catalogs": {}, "appearances": {}}
        self._load()

    def _load(self):
        with self.lock:
            if self.path.exists():
                try:
                    self.data = json.loads(self.path.read_text(encoding="utf-8"))
                except Exception:
                    backup = self.path.with_suffix(self.path.suffix + ".broken")
                    try:
                        self.path.replace(backup)
                    except Exception:
                        pass
                    self.data = {"catalogs": {}, "appearances": {}}

    def _save(self):
        with self.lock:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            tmp = self.path.with_suffix(self.path.suffix + ".tmp")
            tmp.write_text(json.dumps(self.data, ensure_ascii=False, indent=2), encoding="utf-8")
            tmp.replace(self.path)

    def get_catalog(self, streamer: str):
        with self.lock:
            return self.data.get("catalogs", {}).get(streamer)

    @staticmethod
    def _safe_segment(value: str) -> str:
        cleaned = "".join(ch for ch in str(value) if ch.isalnum() or ch in ("-", "_", "."))
        return cleaned[:120] or "item"

    def _write_preview_png(self, streamer: str, form_id: str, kind: str, key: str, data_b64: str | None):
        if not data_b64:
            return None
        try:
            raw = base64.b64decode(data_b64, validate=True)
            if not raw.startswith(b"\x89PNG\r\n\x1a\n"):
                return None
            # Keep local cache bounded against malformed payloads. Follower UI thumbnails are tiny.
            if len(raw) > 2 * 1024 * 1024:
                return None
            folder = self.preview_root / self._safe_segment(streamer) / self._safe_segment(form_id) / self._safe_segment(kind)
            folder.mkdir(parents=True, exist_ok=True)
            filename = self._safe_segment(key or "default") + ".png"
            target = folder / filename
            tmp = target.with_suffix(".png.tmp")
            tmp.write_bytes(raw)
            tmp.replace(target)
            return f"/preview-assets/{urllib.parse.quote(self._safe_segment(streamer))}/{urllib.parse.quote(self._safe_segment(form_id))}/{urllib.parse.quote(self._safe_segment(kind))}/{urllib.parse.quote(filename)}"
        except Exception as exc:
            print(f"[LOCAL] preview decode failed streamer={streamer} form={form_id} kind={kind} key={key}: {exc}")
            return None

    def put_catalog(self, streamer: str, payload: dict[str, Any]):
        # Accept both System.Text.Json camelCase and accidental PascalCase payloads.
        # Store one canonical camelCase contract for the viewer frontend.
        def pick(obj: dict[str, Any], camel: str, pascal: str, default=None):
            if camel in obj:
                return obj.get(camel)
            if pascal in obj:
                return obj.get(pascal)
            return default

        raw_forms = pick(payload, "forms", "Forms", []) or []
        forms = []
        for raw in raw_forms:
            if not isinstance(raw, dict):
                continue
            form_id = pick(raw, "formId", "FormId", "") or ""
            form_preview_b64 = pick(raw, "formPreviewPngBase64", "FormPreviewPngBase64", None)
            variant_preview_b64 = pick(raw, "variantPreviewPngBase64", "VariantPreviewPngBase64", {}) or {}
            color_hex = pick(raw, "colorHexById", "ColorHexById", {}) or {}
            preview_url = self._write_preview_png(streamer, form_id, "form", "default", form_preview_b64)
            variant_preview_urls = {}
            if isinstance(variant_preview_b64, dict):
                for variant_id, encoded in variant_preview_b64.items():
                    url = self._write_preview_png(streamer, form_id, "variant", str(variant_id), encoded)
                    if url:
                        variant_preview_urls[str(variant_id)] = url
            forms.append({
                "formId": form_id,
                "displayName": pick(raw, "displayName", "DisplayName", "") or "",
                "isUnlocked": bool(pick(raw, "isUnlocked", "IsUnlocked", True)),
                "isSpecial": bool(pick(raw, "isSpecial", "IsSpecial", False)),
                "isModded": bool(pick(raw, "isModded", "IsModded", False)),
                "variantIds": [str(x) for x in (pick(raw, "variantIds", "VariantIds", []) or [])],
                "colorIds": [str(x) for x in (pick(raw, "colorIds", "ColorIds", []) or [])],
                "previewUrl": preview_url,
                "variantPreviewUrls": variant_preview_urls,
                "colorHexById": {str(k): str(v) for k, v in color_hex.items()} if isinstance(color_hex, dict) else {},
            })

        allowed = pick(payload, "allowedFormIds", "AllowedFormIds", []) or []
        normalized = {
            "saveId": pick(payload, "saveId", "SaveId", "unknown") or "unknown",
            "generatedAt": pick(payload, "generatedAt", "GeneratedAt", None),
            "forms": forms,
            "allowedFormIds": [str(x) for x in allowed],
        }

        # Splash/main-menu state is transient. Never overwrite a valid viewer
        # catalog with save=unknown/forms=0.
        if normalized["saveId"] == "unknown" or len(normalized["forms"]) == 0:
            with self.lock:
                existing = self.data.get("catalogs", {}).get(streamer)
            return existing if existing is not None else normalized

        with self.lock:
            self.data.setdefault("catalogs", {})[streamer] = normalized
            self._save()
        return normalized

    def get_appearance(self, streamer: str, viewer: str):
        with self.lock:
            return self.data.get("appearances", {}).get(streamer, {}).get(viewer)

    def put_appearance(self, streamer: str, viewer: str, payload: dict[str, Any]):
        with self.lock:
            self.data.setdefault("appearances", {}).setdefault(streamer, {})[viewer] = payload
            self._save()


class App:
    def __init__(self, host: str, port: int, mock: bool, data_file: Path):
        self.host = host
        self.port = port
        self.mock = mock
        self.base_url = f"http://{host}:{port}"
        self.config_source = "mock"
        self.viewer_client_id = ""
        self.viewer_client_secret = ""
        if not mock:
            config = load_chzzk_configuration()
            self.config_source = config.source
            self.viewer_client_id = config.mylamb.client_id
            self.viewer_client_secret = config.mylamb.client_secret
        self.redirect_uri = os.environ.get("COTL_LOCAL_CHZZK_REDIRECT_URI", self.base_url + "/auth/chzzk/callback")
        self.store = Store(data_file, DEFAULT_PREVIEW_ROOT)
        self.token_secret = self._load_secret()
        self.frontend = self._load_frontend()

        if not mock and (not self.viewer_client_id or not self.viewer_client_secret):
            raise ConfigurationError("My Lamb CHZZK credentials could not be loaded.")

    def _load_secret(self) -> bytes:
        env = os.environ.get("COTL_LOCAL_TOKEN_SECRET")
        if env:
            return env.encode()
        DEFAULT_SECRET_FILE.parent.mkdir(parents=True, exist_ok=True)
        if DEFAULT_SECRET_FILE.exists():
            return DEFAULT_SECRET_FILE.read_bytes()
        secret = secrets.token_bytes(32)
        DEFAULT_SECRET_FILE.write_bytes(secret)
        return secret

    def _load_frontend(self) -> bytes:
        html = FRONTEND_FILE.read_text(encoding="utf-8")
        # Same-origin API in local mode. Also enable a visible mock-login button only in --mock mode.
        # Frontend builds have used both spaced and minified API_BASE declarations.
        # Replace the placeholder itself as the final safety net so local mode can never
        # request /__API_BASE__/... by accident.
        html = html.replace("const API_BASE = '__API_BASE__';", "const API_BASE = '';\nconst LOCAL_MOCK_MODE = " + ("true" if self.mock else "false") + ";")
        html = html.replace("const API_BASE='__API_BASE__';", "const API_BASE='';\nconst LOCAL_MOCK_MODE=" + ("true" if self.mock else "false") + ";")
        html = html.replace("__API_BASE__", "")
        html = html.replace(
            "document.querySelector('#loginBtn').onclick=()=>location.href=`${API_BASE}/auth/chzzk/start?streamer=${encodeURIComponent(streamer)}`;",
            "document.querySelector('#loginBtn').onclick=()=>location.href=LOCAL_MOCK_MODE ? `${API_BASE}/dev/login?streamer=${encodeURIComponent(streamer)}&viewer=dev-viewer&nickname=${encodeURIComponent('로컬시청자')}` : `${API_BASE}/auth/chzzk/start?streamer=${encodeURIComponent(streamer)}`;"
        )
        return html.encode("utf-8")

    def sign_token(self, payload: dict[str, Any], ttl_seconds: int) -> str:
        data = dict(payload)
        data["exp"] = int(time.time()) + ttl_seconds
        encoded = b64u(json.dumps(data, separators=(",", ":"), ensure_ascii=False).encode())
        sig = b64u(hmac.new(self.token_secret, encoded.encode(), hashlib.sha256).digest())
        return f"{encoded}.{sig}"

    def verify_token(self, token: str, required_role: str | None = None):
        try:
            encoded, signature = token.split(".", 1)
            expected = b64u(hmac.new(self.token_secret, encoded.encode(), hashlib.sha256).digest())
            if not hmac.compare_digest(signature, expected):
                raise ValueError("bad signature")
            payload = json.loads(unb64u(encoded))
            if int(payload.get("exp", 0)) <= int(time.time()):
                raise ValueError("expired")
            if required_role and payload.get("role") != required_role:
                raise ValueError("wrong role")
            return payload
        except Exception as exc:
            raise PermissionError("invalid session token") from exc

    @staticmethod
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

    def chzzk_me(self, access_token: str):
        if self.mock and access_token == "local-mock-companion":
            return {"channelId": "dev-local-streamer", "channelName": "Local Development"}
        envelope = self.http_json("GET", CHZZK_BASE + "/open/v1/users/me", token=access_token)
        content = envelope.get("content")
        if not content:
            raise RuntimeError(envelope.get("message") or "CHZZK /users/me returned no content")
        return content

    def exchange_code(self, code: str, state: str):
        envelope = self.http_json("POST", CHZZK_BASE + "/auth/v1/token", {
            "grantType": "authorization_code",
            "clientId": self.viewer_client_id,
            "clientSecret": self.viewer_client_secret,
            "code": code,
            "state": state,
        })
        content = envelope.get("content")
        if not content or not content.get("accessToken"):
            raise RuntimeError(envelope.get("message") or "CHZZK token exchange failed")
        return content["accessToken"]

    @staticmethod
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


class Handler(BaseHTTPRequestHandler):
    server_version = "COTLLocal/0.1"

    @property
    def app(self) -> App:
        return self.server.app  # type: ignore[attr-defined]

    def log_message(self, fmt, *args):
        print("[LOCAL] " + (fmt % args))

    def _send_json(self, status: int, body: Any):
        payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("content-type", "application/json; charset=utf-8")
        self.send_header("cache-control", "no-store")
        self.send_header("content-length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def _redirect(self, url: str):
        self.send_response(302)
        self.send_header("location", url)
        self.send_header("cache-control", "no-store")
        self.end_headers()

    def _body_json(self):
        # BaseHTTPRequestHandler does not decode Transfer-Encoding: chunked for us.
        # Accept both normal Content-Length requests and chunked HTTP/1.1 requests so
        # the local dev server behaves like API Gateway/Lambda would in production.
        transfer_encoding = (self.headers.get("transfer-encoding") or "").lower()
        content_length = self.headers.get("content-length")

        if "chunked" in transfer_encoding:
            chunks = []
            while True:
                size_line = self.rfile.readline().strip()
                if not size_line:
                    continue
                # A chunk size may contain extensions after ';'.
                size = int(size_line.split(b";", 1)[0], 16)
                if size == 0:
                    # Consume trailer headers up to the empty line.
                    while True:
                        trailer = self.rfile.readline()
                        if trailer in (b"\r\n", b"\n", b""):
                            break
                    break
                chunks.append(self.rfile.read(size))
                # Consume CRLF after each chunk.
                self.rfile.read(2)
            raw = b"".join(chunks)
            framing = "chunked"
        else:
            length = int(content_length or "0")
            raw = self.rfile.read(length) if length else b"{}"
            framing = f"content-length:{length}"

        print(f"[LOCAL] request body framing={framing}, bytes={len(raw)}, contentType={self.headers.get('content-type', '')}")
        return json.loads(raw.decode("utf-8") or "{}")

    def _bearer(self):
        value = self.headers.get("authorization", "")
        if not value.lower().startswith("bearer "):
            raise PermissionError("missing bearer token")
        return value[7:].strip()

    def do_OPTIONS(self):
        self.send_response(204)
        self.end_headers()

    def do_GET(self):
        self._dispatch("GET")

    def do_POST(self):
        self._dispatch("POST")

    def do_PUT(self):
        self._dispatch("PUT")

    def _dispatch(self, method: str):
        try:
            parsed = urllib.parse.urlparse(self.path)
            path = parsed.path
            qs = urllib.parse.parse_qs(parsed.query)
            q = lambda name, default="": qs.get(name, [default])[0]

            if path in ("/", "/index.html") and method == "GET":
                self.send_response(200)
                self.send_header("content-type", "text/html; charset=utf-8")
                self.send_header("cache-control", "no-store")
                self.send_header("content-length", str(len(self.app.frontend)))
                self.end_headers()
                self.wfile.write(self.app.frontend)
                return

            if path.startswith("/preview-assets/") and method == "GET":
                rel = path[len("/preview-assets/"):].strip("/")
                parts_preview = [urllib.parse.unquote(x) for x in rel.split("/") if x]
                if len(parts_preview) != 4:
                    return self._send_json(404, {"error": "preview not found"})
                streamer_seg, form_seg, kind_seg, file_seg = (Store._safe_segment(x) for x in parts_preview)
                target = (self.app.store.preview_root / streamer_seg / form_seg / kind_seg / file_seg).resolve()
                root = self.app.store.preview_root.resolve()
                if root not in target.parents or not target.is_file() or target.suffix.lower() != ".png":
                    return self._send_json(404, {"error": "preview not found"})
                payload = target.read_bytes()
                self.send_response(200)
                self.send_header("content-type", "image/png")
                self.send_header("cache-control", "public, max-age=3600")
                self.send_header("content-length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
                return

            if path == "/health" and method == "GET":
                return self._send_json(200, {"ok": True, "service": "cotl-companion-local-api", "mock": self.app.mock})

            if path == "/dev/login" and method == "GET":
                if not self.app.mock:
                    return self._send_json(404, {"error": "not found"})
                streamer = q("streamer")
                viewer = q("viewer", "dev-viewer")
                nickname = q("nickname", "로컬시청자")
                token = self.app.sign_token({"role": "viewer", "sub": viewer, "nickname": nickname}, 12 * 60 * 60)
                return self._redirect(f"/?streamer={urllib.parse.quote(streamer)}#token={urllib.parse.quote(token)}")

            if path == "/auth/chzzk/start" and method == "GET":
                streamer = q("streamer")
                if not streamer:
                    return self._send_json(400, {"error": "streamer is required"})
                state = self.app.sign_token({"role": "oauth-state", "streamer": streamer}, 600)
                url = CHZZK_AUTH + "?" + urllib.parse.urlencode({
                    "clientId": self.app.viewer_client_id,
                    "redirectUri": self.app.redirect_uri,
                    "state": state,
                })
                return self._redirect(url)

            if path == "/auth/chzzk/callback" and method == "GET":
                code, state = q("code"), q("state")
                oauth_state = self.app.verify_token(state, "oauth-state")
                access_token = self.app.exchange_code(code, state)
                me = self.app.chzzk_me(access_token)
                token = self.app.sign_token({"role": "viewer", "sub": me["channelId"], "nickname": me.get("channelName", "")}, 12 * 60 * 60)
                return self._redirect(f"/?streamer={urllib.parse.quote(oauth_state['streamer'])}#token={urllib.parse.quote(token)}")

            if path == "/companion/session" and method == "POST":
                me = self.app.chzzk_me(self._bearer())
                token = self.app.sign_token({"role": "companion", "sub": me["channelId"], "nickname": me.get("channelName", "")}, 60 * 60)
                return self._send_json(200, {"token": token, "streamerChannelId": me["channelId"]})

            parts = [x for x in path.split("/") if x]
            if len(parts) >= 3 and parts[0] == "streamers":
                streamer = urllib.parse.unquote(parts[1])

                if parts[2] == "catalog":
                    if method == "GET":
                        catalog = self.app.store.get_catalog(streamer)
                        return self._send_json(404, {"error": "catalog not available"}) if catalog is None else self._send_json(200, catalog)
                    if method == "PUT":
                        session = self.app.verify_token(self._bearer(), "companion")
                        if session["sub"] != streamer:
                            raise PermissionError("streamer mismatch")
                        body = self._body_json()
                        raw_save = body.get("saveId", body.get("SaveId", "unknown"))
                        raw_forms = body.get("forms", body.get("Forms", [])) or []
                        raw_allowed = body.get("allowedFormIds", body.get("AllowedFormIds", [])) or []
                        print(f"[LOCAL] catalog PUT received: streamer={streamer}, rawSave={raw_save}, rawForms={len(raw_forms)}, rawAllowed={len(raw_allowed)}, keys={sorted(body.keys())}")

                        # Never acknowledge a transient splash/main-menu catalog as a successful publish.
                        # Returning 409 makes the Companion retry instead of printing a false success.
                        if not raw_save or raw_save == "unknown" or len(raw_forms) == 0:
                            print(f"[LOCAL] catalog rejected: transient/empty payload save={raw_save}, forms={len(raw_forms)}")
                            return self._send_json(409, {"ok": False, "error": "transient catalog", "saveId": raw_save, "forms": len(raw_forms), "allowed": len(raw_allowed)})

                        catalog = self.app.store.put_catalog(streamer, body)
                        stored_forms = len(catalog.get('forms') or [])
                        stored_allowed = len(catalog.get('allowedFormIds') or [])
                        stored_save = catalog.get('saveId')
                        preview_forms = sum(1 for f in (catalog.get('forms') or []) if f.get('previewUrl'))
                        variant_previews = sum(len(f.get('variantPreviewUrls') or {}) for f in (catalog.get('forms') or []))
                        palette_colors = sum(len(f.get('colorHexById') or {}) for f in (catalog.get('forms') or []))
                        print(f"[LOCAL] catalog stored+verified: streamer={streamer}, save={stored_save}, forms={stored_forms}, allowed={stored_allowed}, previewForms={preview_forms}, variantPreviews={variant_previews}, paletteColors={palette_colors}")
                        return self._send_json(200, {"ok": True, "saveId": stored_save, "forms": stored_forms, "allowed": stored_allowed, "previewForms": preview_forms, "variantPreviews": variant_previews, "paletteColors": palette_colors})

                if len(parts) == 4 and parts[2] == "appearance" and parts[3] == "me":
                    session = self.app.verify_token(self._bearer(), "viewer")
                    if method == "GET":
                        item = self.app.store.get_appearance(streamer, session["sub"])
                        return self._send_json(404, {"error": "appearance not set"}) if item is None else self._send_json(200, item)
                    if method == "PUT":
                        catalog = self.app.store.get_catalog(streamer)
                        if not catalog:
                            return self._send_json(409, {"error": "streamer catalog is not available yet"})
                        body = self._body_json()
                        appearance = body.get("appearance") or body
                        self.app.validate_appearance(catalog, appearance)
                        payload = {
                            "viewerChannelId": session["sub"],
                            "viewerNickname": session.get("nickname", ""),
                            "appearance": appearance,
                            "updatedAt": int(time.time()),
                        }
                        self.app.store.put_appearance(streamer, session["sub"], payload)
                        return self._send_json(200, payload)

                if len(parts) == 5 and parts[2] == "viewers" and parts[4] == "appearance" and method == "GET":
                    viewer = urllib.parse.unquote(parts[3])
                    session = self.app.verify_token(self._bearer(), "companion")
                    if session["sub"] != streamer:
                        raise PermissionError("streamer mismatch")
                    item = self.app.store.get_appearance(streamer, viewer)
                    return self._send_json(404, {"error": "appearance not set"}) if item is None else self._send_json(200, item)

            return self._send_json(404, {"error": "not found", "path": path})

        except PermissionError as exc:
            return self._send_json(401, {"error": str(exc)})
        except ValueError as exc:
            return self._send_json(400, {"error": str(exc)})
        except Exception as exc:
            print("[LOCAL] ERROR", repr(exc))
            return self._send_json(500, {"error": "internal error", "detail": str(exc)})


def main():
    parser = argparse.ArgumentParser(description="Local My Lamb API + frontend")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=17882)
    parser.add_argument("--mock", action="store_true", help="Use local fake viewer/companion identities; no CHZZK call for smoke tests")
    parser.add_argument("--data", default=str(DEFAULT_DATA_FILE))
    args = parser.parse_args()

    app = App(args.host, args.port, args.mock, Path(args.data))
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.app = app  # type: ignore[attr-defined]

    print("COTL Companion Local Appearance Server - devbridge10a")
    print(f"[LOCAL] API:      {app.base_url}")
    print(f"[LOCAL] Frontend: {app.base_url}/")
    print(f"[LOCAL] Data:     {app.store.path}")
    print(f"[LOCAL] Previews: {app.store.preview_root}")
    if args.mock:
        print("[LOCAL] MODE=MOCK (viewer login is local; no CHZZK OAuth required for the web UI)")
        print("[LOCAL] Companion mock bearer for API-only tests: local-mock-companion")
    else:
        print(f"[LOCAL] MODE=CHZZK LIVE (config={app.config_source})")
        print(f"[LOCAL] Register this redirect URI in the CHZZK developer app: {app.redirect_uri}")
    print("[LOCAL] Ctrl+C to stop")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
