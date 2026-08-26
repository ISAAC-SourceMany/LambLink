# RC19 safe GAME_STATUS synchronization

## Observed RC18 failure

The game Mod and Companion completed the localhost WebSocket connection, but Companion remained:

```text
GAME=True, SAVE=unknown, CATALOG=0, CATALOG_SAVE=unknown
```

The matching game log ended in this order:

```text
[BRIDGE][CONNECTED] Connected to CHZZK Companion ...
[BRIDGE][STATE] initial synchronization requested after connect
Resolved active save: slot_0 via SaveAndLoad.SAVE_SLOT
```

`TryStartGameStatusSend()` logs `TX-START` only after `BuildGameStatusSafely()` returns. Because the
save resolver completed but `TX-START` never appeared, the failure is after save resolution and
before WebSocket transmission. In RC18 that remaining path evaluated optional game-version and
dungeon-area metadata. The same metadata construction was also present in RC14 and RC16.

## Correction

`GAME_STATUS` gates all later catalog and roster requests, so RC19 limits the mandatory snapshot to:

- whether `PlayerFarming.Instance` is available;
- the active save ID;
- the Mod version;
- a conservative `BASE`/`UNKNOWN` area marker.

Game-version and runtime dungeon-area evaluation are no longer performed before the mandatory
status send. Donation execution remains safe: the Mod still evaluates the actual runtime area when
the donation is applied and corrects a stale Companion selection before mutating the game.

Companion now also logs successful delivery of `GET_GAME_STATUS`, distinguishing Companion-to-Mod
transport from Mod-side snapshot construction.

## Required runtime sequence

Companion:

```text
[BRIDGE][STATE][TX] GET_GAME_STATUS
[BRIDGE][STATE][TX-OK] GET_GAME_STATUS delivered to Mod socket
[BRIDGE][STATE][RX] GAME_STATUS inGame=True, save=slot_0, ...
[BRIDGE][SYNC] requesting appearance catalog and follower roster for save=slot_0
[APPEARANCE] loaded <N> forms for save slot_0 (changed)
```

Game log:

```text
[BUILD=rc19-safe-game-status-sync]
[BRIDGE][STATE][TX-START] reason=connection-handshake, inGame=True, save=slot_0, area=BASE
[BRIDGE][STATE][TX-OK] reason=connection-handshake, inGame=True, save=slot_0, area=BASE
[APPEARANCE][TX] save=slot_0, forms=<N>
```

`[BRIDGE][STATE] GET_GAME_STATUS received from Companion` may appear before or after `TX-START`.
The Mod also initiates a status snapshot from its connection callback, so that request-received line
is diagnostic rather than a prerequisite when the complete TX/RX sequence is present.

Final status:

```text
GAME=True, SAVE=slot_0, CATALOG=<non-zero>, CATALOG_SAVE=slot_0
```

Do not continue to raffle testing if `TX-START`, Mod `TX-OK`, Companion `RX`, or catalog transmission
is missing. Those checkpoints identify the direction and stage that failed.
