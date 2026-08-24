# RC22 persistent runtime host

## Runtime evidence

RC21 connected its WebSocket and Companion successfully sent `GET_GAME_STATUS`, but the Mod log
contained no receive-wait or queue record. The independent watchdog then reported:

```text
[DIAG][WATCHDOG][NO-UPDATE] ... queue=0, socketConnected=False
```

This proves the BepInEx plugin component's `Update()` was never invoked in that game startup.
The bridge lifetime was also tied to that component, so its socket closed immediately afterward.

## Correction

- Create a hidden `CHZZK Companion Runtime Host` GameObject during plugin `Awake()`.
- Mark it `DontDestroyOnLoad` and run the main-thread dispatcher exclusively from its `Update()`.
- Retain the dispatcher delegate independently of Unity's destroyed-object null semantics.
- Do not cancel the bridge or watchdog from `Plugin.OnDestroy()`.
- Keep reconnecting the Mod socket after abnormal closure.
- Log full WebSocket exception chains and final socket states on both ends.

## Required startup sequence

```text
[BUILD=rc22-persistent-runtime-host]
[DIAG][RUNTIME-HOST][INSTALLED]
[DIAG][RUNTIME-HOST][FIRST-UPDATE]
[DIAG][UPDATE][FIRST] source=persistent-runtime-host
[BRIDGE][CONNECTED]
[BRIDGE][RX][WAIT]
[BRIDGE][RX][FRAME] ... type=GET_GAME_STATUS
```

After a valid status response, Companion must request the appearance catalog and roster. The final
acceptance state is `GAME=True`, `SAVE=slot_...`, and `CATALOG>0`.
