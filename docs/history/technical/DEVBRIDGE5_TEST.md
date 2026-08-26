# devbridge5 test checklist

## 1. Build

Build the full Visual Studio solution. Copy the new Mod + Protocol DLLs into `BepInEx/plugins`.

Expected Mod version: `0.1.3`.

## 2. Normal recruit raffle (important)

Do **not** use `dev spawn` for this test.

1. Play until the game itself creates a recruit waiting for indoctrination.
2. Companion should print:

```text
[RAFFLE] game recruit detected: ID=<id>, save=slot_0
[RAFFLE] OPEN 30s — chat: !신도
```

3. Join with CHZZK chat `!신도` (or `dev join name` during local testing).
4. Allow the timer to draw or run `raffle draw`.
5. Expected:

```text
[RAFFLE] winner: <nickname> -> recruit <same id>
[RAFFLE] identity applied: <nickname> -> recruit <same id>
```

There must still be only the original one recruit.

## 3. Queue test

If the game has multiple pending recruit IDs, Companion should print the first as active and the rest as queued:

```text
[RAFFLE] queued game recruit 23; queue=1
```

After the current identity result completes, the next queued recruit begins its raffle.

## 4. dev spawn

`dev spawn <nickname>` is retained only as a low-level development test for `CreateNewRecruit`. It is not the production viewer flow and should not be used while another recruit is waiting.
