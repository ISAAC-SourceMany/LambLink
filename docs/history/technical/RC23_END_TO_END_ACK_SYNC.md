# RC23 end-to-end ACK and synchronization

## Why RC22 was not sufficient

RC21 proved that a WebSocket can be connected while the BepInEx plugin receives no Unity
`Update` calls. RC22 moved dispatch to a persistent GameObject, which addresses that failure,
but two observable states were still conflated:

- `GAME=True` meant only that the WebSocket was open.
- a successful raffle WebSocket write was logged as delivery even though Companion had not
  acknowledged dispatching the request.

Those assumptions could reproduce the same user-visible failure after a short disconnect.

## RC23 state synchronization

`GameStatusEvent` now includes `RuntimePumpActive` and `RuntimeUpdateCount`. A status built on the
persistent Unity main-thread pump sets `RuntimePumpActive=true`. The network-cache fallback reports
whether a recent pump tick has actually been observed instead of treating socket activity as game
readiness.

Companion uses the following phases:

1. `SOCKET_CONNECTED`
2. `WAITING_FOR_RUNTIME_PUMP` — resend `GET_GAME_STATUS` every 2 seconds
3. `SAVE_READY`
4. `WAITING_FOR_CATALOG` — resend catalog and roster requests every 5 seconds
5. `READY` — active save and a non-empty catalog were received

`status` reports both `GAME_SOCKET` and `GAME_READY`. The compatibility field `GAME` equals
`GAME_READY`, not the raw socket state. A pump status older than 15 seconds is not ready.

## RC23 raffle delivery

The Mod retains every automatic `RAFFLE_REQUESTED` event until it receives
`RAFFLE_REQUEST_ACK`. Companion treats repeated requests idempotently and returns one of:

- `started`
- `queued`
- `already-active`
- `already-queued`
- `invalid-recruit-id`

If the ACK is lost, the Mod retries after 2 seconds. Companion recognizes the duplicate and sends
another accepted ACK without opening a second raffle.

## Runtime ownership

The persistent host still survives ordinary `Plugin.OnDestroy`. It now owns application shutdown
and replacement shutdown. If another Plugin instance is created, the old bridge and watchdog are
cancelled before the host switches to the new dispatcher, preventing duplicate localhost sockets.

## Required runtime proof

Compilation alone is not sufficient. A successful Windows/COTL run must show:

```text
[DIAG][RUNTIME-HOST][FIRST-UPDATE]
[BRIDGE][STATE][RX] GAME_STATUS pump=True
SYNC=READY
[RAFFLE][ACK] ... accepted=true
```

The viewer catalog update is then verified by `[CLOUD] catalog uploaded+verified` and the viewer
page polling the changed catalog.
