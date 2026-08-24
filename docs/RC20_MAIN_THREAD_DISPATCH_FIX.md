# RC20 main-thread dispatcher fix

## Evidence from RC19

Companion confirmed that `GET_GAME_STATUS` was written to the connected Mod socket:

```text
[BRIDGE][STATE][TX-OK] GET_GAME_STATUS delivered to Mod socket
```

The game log confirmed the RC19 Mod and WebSocket connection, but did not contain either of these:

```text
[BRIDGE][STATE] GET_GAME_STATUS received from Companion
[BRIDGE][STATE][TX-START]
```

RC19 had already removed every optional probe after save resolution. Therefore the prior diagnosis
that status payload construction was blocking was contradicted by the RC19 runtime result.

The remaining execution order in `Plugin.Update()` was:

1. dequeue any command already received;
2. run the recurring pending-recruit cleanup;
3. globally scan every `FollowerRecruit` with `FindObjectsOfType`;
4. only then start GAME_STATUS synchronization.

The first Update can reach step 3 before the networking continuation enqueues `GET_GAME_STATUS`.
When the global Unity object scan does not return on the affected runtime, the later command remains
queued and the connection callback's pending status snapshot also never reaches `TX-START`.

## RC20 correction

- The recurring global pending-recruit cleanup was removed from `Plugin.Update()`.
- Mandatory status synchronization now runs immediately after command dispatch.
- `GetPendingRecruitIds()` uses the authoritative `DataManager.Instance.Followers_Recruit` list and
  no longer performs a global Unity object scan.
- The indoctrination hook resolves its `Follower` argument before consulting any fallback list.
- The Mod networking thread logs every successfully decoded and queued command as
  `[BRIDGE][RX-QUEUED]`, allowing socket receipt to be distinguished from main-thread execution.

Global `FollowerRecruit` scans remain only inside development-spawn recovery or targeted fallback
methods that are not part of the normal release heartbeat/catalog path.

## Required sequence

Game log:

```text
[BUILD=rc20-main-thread-scan-fix]
[BRIDGE][RX-QUEUED] type=GET_GAME_STATUS
[BRIDGE][STATE] GET_GAME_STATUS received from Companion
[BRIDGE][STATE][TX-START] reason=connection-handshake, inGame=True, save=slot_0, area=BASE
[BRIDGE][STATE][TX-OK] reason=connection-handshake, inGame=True, save=slot_0, area=BASE
[BRIDGE][RX-QUEUED] type=GET_FOLLOWER_APPEARANCE_CATALOG
[APPEARANCE][TX] save=slot_0, forms=<N>
```

Companion:

```text
[BRIDGE][STATE][TX-OK] GET_GAME_STATUS delivered to Mod socket
[BRIDGE][STATE][RX] GAME_STATUS inGame=True, save=slot_0, ...
[BRIDGE][SYNC] requesting appearance catalog and follower roster for save=slot_0
[APPEARANCE] loaded <N> forms for save slot_0 (changed)
```

Final status:

```text
GAME=True, SAVE=slot_0, CATALOG=<non-zero>, CATALOG_SAVE=slot_0
```
