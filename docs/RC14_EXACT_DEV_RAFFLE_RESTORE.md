# RC14 exact dev raffle restore

RC13 proved that `ShowIndoctrinationMenu(Follower, OriginalFollowerLookData)` Prefix fires in Release, but the refactored `NotifyIndoctrinationMenuOpened -> RequestRaffleForCurrentRecruit -> TrySendAsync` path did not produce a request.

RC14 restores the **exact dev10z proven raffle-start flow** for this hook:

1. require connected bridge/services
2. resolve recruit directly from Harmony `__args` (expected `source=arg:Follower`)
3. dedupe via handled/announced sets
4. log `CHZZK raffle requested at indoctrination start...`
5. call `ModBridgeClient.SendAsync(RaffleRequested)` immediately

Extra `[RAFFLE][DEV-RESTORE]` diagnostics identify argument types, recruit resolution, prerequisites and exceptions.
