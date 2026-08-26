# RC21 corrective dispatch, watchdog, and cached status fallback

## Confirmed evidence from RC19

- Companion completed the WebSocket send of `GET_GAME_STATUS`.
- The Mod socket was connected.
- The game had already resolved `slot_0` internally.
- Companion never received `GAME_STATUS`; therefore catalog and roster requests were never issued.

RC20 removed a global recruit scan, but its `Update()` still called `_followers.Tick()` before
draining the bridge queue. Its source comment and actual order did not match.

## RC21 correction

1. Drain the bridge command queue before optional follower/UI maintenance.
2. When the Mod network thread decodes `GET_GAME_STATUS`, immediately send a snapshot using only
   thread-safe cached values. No Unity API is called from this fallback.
3. Continue with the authoritative Unity-main-thread status and normal catalog/roster requests.
4. Run a separate watchdog. If Unity never invokes `Update`, log `NO-UPDATE`; if a main-thread call
   does not return for five seconds, log the exact `stage=` value.
5. Record catalog progress down to an individual form ID.

## One-run decision table

| Last evidence | Exact boundary | Next correction |
|---|---|---|
| Companion `[Bridge][TX][BEGIN]` without `[END]` | Companion WebSocket send | Replace/reconnect socket and inspect send lock |
| Companion TX `END`, no Mod `[RX][FRAME]` | Local WebSocket delivery | Inspect stale/replaced socket and close handshake |
| Mod RX `FRAME`, no `QUEUED` | JSON decode | Use `DECODE-FAILED` exception and payload size |
| Mod `QUEUED`, watchdog `NO-UPDATE` | BepInEx/Unity lifecycle | Move dispatcher to a dedicated persistent Unity component |
| Mod `QUEUED`, watchdog `STALLED stage=...` | Named Unity call | Remove or defer only that named call |
| Fallback TX `OK`, no Companion RX `FRAME` | Mod-to-Companion delivery | Inspect Companion receive loop/socket replacement |
| Companion receives status, no catalog TX | Companion status dispatch | Use Companion `DISPATCH BEGIN/END/FAILED` |
| Catalog build lacks `END` | Catalog stage/form | Use watchdog `stage=CATALOG/...` to isolate the exact getter/form |
| Catalog TX `END`, no Companion catalog RX | Mod-to-Companion catalog transport | Inspect payload size/socket receive failure |
| Catalog RX and upload failure | Cloud publication | Diagnose only the cloud request; game bridge is already proven |

## Required matched pair

Run `build-test-pair.ps1`, use Companion `v1.0.0-rc21`, and install the two DLLs from
`dist\rc21-plugin`. Do not mix this Mod with an installed RC14/RC18/RC19 Companion because the
numbered transport evidence must exist on both ends.

After one test, collect these two complete files:

- `Cult of the Lamb\BepInEx\LogOutput.log`
- `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc21.log`
