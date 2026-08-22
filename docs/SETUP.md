# Developer setup

## 1. Requirements

- Windows 10/11
- Steam Cult of the Lamb
- Visual Studio 2022 or Rider
- .NET 8 SDK
- A CHZZK developer application with redirect URI `http://127.0.0.1:17881/callback/`
- BepInEx/Cult of the Lamb game assemblies for the mod project

## 2. Environment variables

```powershell
$env:CHZZK_CLIENT_ID="..."
$env:CHZZK_CLIENT_SECRET="..."
$env:CHZZK_REDIRECT_URI="http://127.0.0.1:17881/callback/"
```

Do not commit these values.

## 3. Mod references

Create `src/ChzzkOfTheLamb.Mod/lib` and place the game-specific compile references there. At minimum the project expects:

- `Assembly-CSharp.dll` (or a publicized compile-only equivalent)
- `BepInEx.dll` / BepInEx core assemblies matching the installed pack
- `0Harmony.dll`
- Unity assemblies required by the game build

The exact list should be aligned to the current Cult of the Lamb mod template/BepInEx pack installed locally.

## 4. Run order (development)

1. Start `ChzzkOfTheLamb.Companion`.
2. Complete browser OAuth.
3. Launch Cult of the Lamb with BepInEx and the mod installed.
4. Companion should report the game bridge connected.
5. Test chat `!신도` and a donation event.

## Development / offline CHZZK mode

For game-bridge testing, CHZZK credentials are no longer required.

If `CHZZK_CLIENT_ID` and `CHZZK_CLIENT_SECRET` are missing, Companion automatically starts in development mode and still opens the local WebSocket bridge at `ws://127.0.0.1:17771/game`.

You can also force this mode even when credentials exist:

```powershell
$env:CHZZK_DEV_MODE="1"
.\ChzzkOfTheLamb.Companion.exe
```

Useful development commands:

```text
status
refresh-forms
forms
dev spawn TestViewer
raffle start
dev join ViewerA
dev join ViewerB
raffle draw
dev donation 1000
```

This mode is for local development only. It does not open CHZZK OAuth and does not receive real CHAT/DONATION/SUBSCRIPTION events.
