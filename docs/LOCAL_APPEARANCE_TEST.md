# devbridge7 - Local My Lamb end-to-end test

This step validates the viewer appearance website and Companion API integration **before AWS deployment**.
The local server intentionally implements the same HTTP contract used by the AWS Lambda backend, but persists data to `local-server/data.json`.

## Ports

- Companion ↔ Mod WebSocket: `127.0.0.1:17771`
- Companion OAuth callback: `127.0.0.1:17881`
- Local My Lamb web/API: `127.0.0.1:17882`

These are separate on purpose.

## Test A - Local UI/API smoke test (no viewer CHZZK OAuth)

1. Open PowerShell in `local-server`.
2. Run `./run-local-mock.ps1`.
3. In a second PowerShell, test health:
   `Invoke-RestMethod http://127.0.0.1:17882/health`
4. This mode is only for testing the web/API contract. It uses the fixed mock viewer `dev-viewer` when the Login button is clicked.

For a complete Companion integration test, use Test B because the normal Companion authenticates its session with a real CHZZK access token.

## Test B - Full local test using real CHZZK OAuth

### 1. Register the local viewer redirect URI

Use two separate CHZZK developer applications:

- `COTL Companion` (streamer): `http://127.0.0.1:17881/callback/`
- `CHZ Viewer Lamb` (viewer web): `http://127.0.0.1:17882/auth/chzzk/callback`

The My Lamb server must use the **CHZ Viewer Lamb** Client ID/Secret only.

### 2. Start the local My Lamb server

In PowerShell:

```powershell
$env:MYLAMB_CHZZK_CLIENT_ID="CHZ_VIEWER_LAMB_CLIENT_ID"
$env:MYLAMB_CHZZK_CLIENT_SECRET="CHZ_VIEWER_LAMB_CLIENT_SECRET"
cd .\local-server
.\run-local-live.ps1
```

Expected:

```text
[LOCAL] API:      http://127.0.0.1:17882
[LOCAL] Frontend: http://127.0.0.1:17882/
[LOCAL] MODE=CHZZK LIVE
```

### 3. Configure and run Companion

Open a new PowerShell window. In this second window, keep using the original `COTL Companion` credentials for the Companion itself:

```powershell
$env:CHZZK_CLIENT_ID="COTL_COMPANION_CLIENT_ID"
$env:CHZZK_CLIENT_SECRET="COTL_COMPANION_CLIENT_SECRET"
```

Then:

```powershell
.\local-server\configure-companion-local.ps1
```

Run the devbridge9 Companion from the **same PowerShell process**.

Expected after Companion OAuth:

```text
[CLOUD] connected: http://127.0.0.1:17882
[CLOUD] viewer setup URL: http://127.0.0.1:17882?streamer=<streamerChannelId>
```

(`CLOUD` is the legacy log label; in this test it points at the local API, not AWS.)

### 4. Load a Cult of the Lamb save and refresh forms

```text
status
refresh-forms
forms
```

When the appearance catalog is non-empty, Companion uploads it to the local server.

### 5. Open the viewer URL

Copy the viewer setup URL printed by Companion into a browser.
Click **치지직으로 로그인**, choose Form / Variant / Color, and save.

The local server writes the result to:

`local-server/data.json`

### 6. Validate the saved appearance is consumed by raffle

When the game presents an indoctrination recruit and the raffle opens, enter `!신도` from the same viewer CHZZK account used on the My Lamb page.
On win, Companion should fetch the saved appearance from the local API and send it with `APPLY_RECRUIT_IDENTITY` to the Mod.

## Important current blocker

The local website can only show meaningful forms after the game-side appearance catalog returns actual forms. If Companion still prints `loaded 0 forms`, the server/web path can be smoke-tested with mock catalog data, but the real viewer customization flow cannot be considered complete until `FollowerAppearanceService` maps the current COTL appearance data correctly.
