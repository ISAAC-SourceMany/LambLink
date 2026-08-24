# RC24 raffle lifecycle and COTL_API load order

## Runtime evidence from RC23

The supplied Mod log proves that the Harmony trigger was installed and fired:

```text
[RAFFLE][PATCH] automatic Harmony target=Lamb.UI.UIManager.ShowIndoctrinationMenu(Follower,OriginalFollowerLookData)
[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired; args=2, bridgeConnected=True
[RAFFLE][QUEUE] recruit=35 ...
[RAFFLE][ACK] recruit=35, accepted=true, status=started ...
```

The supplied Companion log proves that the application received and opened the round:

```text
[RAFFLE] game recruit detected: ID=35, save=slot_0
[RAFFLE] OPEN 30s — chat: !신도
```

After the empty round completed, opening the same pending recruit again reached the Harmony prefix
but was rejected by the Mod:

```text
[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired; args=2, bridgeConnected=True
Indoctrination raffle trigger ignored for recruit 35 (already handled/announced/pending).
```

RC23 added the ID to `_announcedRecruitIds` after the start ACK but had no round-finished message
that removed it. That is the confirmed state-lifetime defect.

## BepInEx and COTL_API integration

COTL_API 0.3.4 declares the official plugin GUID `io.github.xhayper.COTL_API` and initializes its
own Harmony patches in `Awake`. It does not expose a public indoctrination-start event. RC24
therefore keeps the game-specific Harmony prefix, pins COTL_API 0.3.4, and adds a hard
`BepInDependency` so COTL_API is initialized first.

After `Harmony.CreateAndPatchAll`, RC24 reads Harmony patch metadata for every resolved
`ShowIndoctrinationMenu` overload and logs whether the CHZZK owner ID is actually present.

## Corrected state machine

- Menu prefix queues `RAFFLE_REQUESTED` only when the recruit is not pending, active, or handled.
- Companion returns `RAFFLE_REQUEST_ACK`; accepted requests become active/announced.
- Companion sends `RAFFLE_ROUND_CLOSED` after cancel, no participants, identity failure, or
  successful identity application.
- Cancel/no participants/identity failure set `AllowRetry=true` and release the recruit guard.
- Successful identity application sets `AllowRetry=false` and marks the recruit handled.
- A ten-minute stale guard is retained only as crash/disconnect recovery if the close message is
  never delivered.

## Required runtime proof

Startup:

```text
[Info : COTL_API] COTL_API loaded!
[RAFFLE][PATCH-VERIFY] ... installed=True
[BUILD=rc24-raffle-lifecycle-cotl-api-order]
```

Round start:

```text
[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired
[RAFFLE][QUEUE]
[RAFFLE][TX-WRITTEN]
[RAFFLE] OPEN 30s
[OVERLAY][RAFFLE-OPEN] ... clientPolling=True
```

Empty/cancelled round and same-recruit retry:

```text
[RAFFLE][ROUND-CLOSED-TX] ... allowRetry=True ... sent=True
[RAFFLE][ROUND-CLOSED] ... allowRetry=True ... releasedAnnouncementGuard=True
[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired
[RAFFLE][QUEUE]
```

If `[RAFFLE] OPEN` exists but `clientPolling=False` or `status` reports
`OVERLAY=not-polling`, the game and raffle are working and the remaining issue is the OBS browser
source not loading `http://127.0.0.1:17883/overlay`.
