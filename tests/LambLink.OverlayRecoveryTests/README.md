# Overlay startup recovery regression

Build the isolated fixture and run the browser tests from the repository root:

```powershell
dotnet build tests/LambLink.OverlayRecoveryTests/LambLink.OverlayRecoveryTests.csproj -c Release
node tests/LambLink.OverlayRecoveryTests/recovery.cjs
```

Requires .NET 8, Node.js, `playwright` (resolvable directly or via `NODE_PATH`), and
Chrome. Override `CHROME_PATH` if needed. Port 17883 must be free. This fixture links
the production overlay server and embedded bootstrap, writes only a unique temporary
directory, and never logs in to CHZZK or connects to the game. It shuts down its own
server/browser and removes its own temporary directory on exit.

Coverage: local file before server startup, repeated initial navigation failures,
real raffle rendering, healthy connection without reloads, server stop/restart,
HTTP 503 state responses, indefinitely stalled state requests, browser reopening,
untrusted heartbeat rejection, donation layout after recovery, stable generated
file path, unchanged-file preservation, and bootstrap file updates.

Chromium automation does not replace the OBS acceptance check:

1. Run the updated Companion once, then close it. In OBS select the generated file
   shown by `overlay` with **Local file** enabled. Keep OBS on the same PC.
2. Open OBS while Companion is closed; the source should remain transparent.
3. Start Companion without refreshing OBS. Verify the log has
   `entry=local-bootstrap`, `state polling active`, and a current document handshake.
4. In a controlled test (no live recruitment), use `raffle start`, check the visible
   countdown, then `raffle cancel`.
5. Stop/restart Companion while OBS stays open. Stale content must disappear and
   the next test raffle must appear without refreshing.
6. Repeat with Companion first; hide/show the source, including with OBS's
   shutdown-when-hidden option enabled. The source reconnects once active again.

Existing direct HTTP sources require one-time migration to the local file.
Changing the bootstrap itself requires one source refresh; server-side overlay
document updates are handled by the child page/version handshake.
