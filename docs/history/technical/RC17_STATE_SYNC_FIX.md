# RC17 state synchronization and reliable raffle delivery

## Confirmed RC14/RC16 Companion comparison

The complete `ChzzkOfTheLamb.Companion` trees were compared before this change.
RC14 and RC16 contained the same files. Every implementation file was byte-identical except:

- `Program.cs`: displayed release label changed from `1.0.0-rc1` to `1.0.0-rc16`.
- `ChzzkOfTheLamb.Companion.csproj`: package/file version metadata changed to RC16.

The Protocol tree, WebSocket server, catalog logic, raffle logic, CHZZK client, cloud client,
overlay, storage, and settings implementations were identical. Rebuilding the RC16 Companion
therefore changed version identity but did not correct the observed `GAME=True, SAVE=unknown,
CATALOG=0` state.

## Root cause

Transport connection and application synchronization were treated as the same state. Companion
reported `GAME=True` as soon as the WebSocket opened, but it did not request current game state.
It waited for a periodic fire-and-forget `GAME_STATUS` message from the Mod. The Mod did not
observe the result of that send. If the first application message was lost or failed, Companion
never populated `currentSaveId`, and its catalog/roster refresh loops deliberately remained gated.

The automatic raffle trigger had a related one-shot failure: if the indoctrination menu opened
while the bridge was not connected, the request was returned/dropped and was never retried.

## RC17 behavior

- Companion sends `GET_GAME_STATUS` immediately after every game WebSocket connection.
- Mod schedules current status creation on Unity's main thread and answers with `GAME_STATUS`.
- Mod also initiates the same state synchronization when its socket connects.
- Status sends are observed and logged as `TX-START`, `TX-OK`, or `TX-FAILED`; failures retry.
- Companion logs receipt as `[BRIDGE][STATE][RX] GAME_STATUS`.
- A valid in-game save immediately triggers appearance catalog and follower roster requests.
- WebSocket send failures are caught and logged on both sides.
- Companion clears a dead socket reference when its receive loop ends, so `GAME=True` cannot
  remain stuck after application receive failure.
- Indoctrination raffle requests are queued even while disconnected and are marked announced only
  after WebSocket delivery succeeds. Failed sends retry while that recruit remains pending.
- Harmony target selection and Prefix execution are both logged.

## Expected startup sequence

Companion:

```text
CHZZK Companion for Cult of the Lamb - v1.0.0-rc17
[GAME] connected
[BRIDGE][STATE][TX] GET_GAME_STATUS
[BRIDGE][STATE][RX] GAME_STATUS inGame=True, save=slot_0, ...
[BRIDGE][SYNC] requesting appearance catalog and follower roster for save=slot_0
[APPEARANCE] loaded <N> forms for save slot_0
```

Game log:

```text
[BUILD=rc17-state-sync-reliable-raffle]
[BRIDGE][CONNECTED] ...
[BRIDGE][STATE][TX-START] reason=connection-handshake, inGame=True, save=slot_0, ...
[BRIDGE][STATE][TX-OK] reason=connection-handshake, inGame=True, save=slot_0, ...
[APPEARANCE][TX] save=slot_0, forms=<N>
```

Automatic raffle:

```text
[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired; ...
[RAFFLE][QUEUE] recruit=<ID>, ...
[RAFFLE][TX-START] recruit=<ID>, save=slot_0
CHZZK raffle requested at indoctrination start for game recruit <ID>; delivery confirmed
```
