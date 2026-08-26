# RC16 bridge regression fix

RC15 preserved the dev10z raffle trigger but also restored its scene-gated bridge startup. On the current Cult of the Lamb build, that gate could remain blocked indefinitely: the mod loaded, but it never attempted `ws://127.0.0.1:17771/game`, leaving Companion at `GAME=False`.

RC16 keeps the dev10z raffle trigger and recruit-ID resolution while restoring the known-good immediate localhost bridge startup from RC14. Game mutations remain queued and continue to execute only from Unity's main-thread `Update` method.

Expected game log order:

```text
CHZZK Companion Integration 1.0.0 loaded [BUILD=rc16-dev10z-raffle-immediate-bridge]
[BRIDGE][START] reason=plugin-awake, endpoint=ws://127.0.0.1:17771/game
[BRIDGE][CONNECT] attempting ws://127.0.0.1:17771/game
[BRIDGE][CONNECTED] Connected to CHZZK Companion at ws://127.0.0.1:17771/game
```

Expected Companion status after connection:

```text
GAME=True
```

An automatic raffle test means opening a new follower's indoctrination/appearance screen in the game without typing `raffle start`. The mod should send `RAFFLE_REQUESTED`, and Companion should print `[RAFFLE] game recruit detected` followed by `[RAFFLE] OPEN`.
