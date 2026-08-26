# RC26 CHZZK nameplate layout/lifecycle fix

## Confirmed RC25 failure

The raffle and identity application succeeded for follower ID 35. Companion synchronized marker
IDs 12 and 35, and the Mod created the badge object. The failure occurred inside the Mod UI layer:

1. `ApplyIdentity` registered the new marker before writing `FollowerInfo.Name`.
2. The immediate nameplate regeneration compared the expected new nickname with the old vanilla
   recruit name and removed the valid marker as if the ID had been reused.
3. A later Companion synchronization recreated the marker, but TMP still reported a transient
   `preferredWidth=2` for the long Korean nickname.
4. The badge was consequently placed only six pixels left of the name center and was hidden by or
   overlapped with the vanilla name.

The existing follower ID 12 showing a green `Chzzk` label proves the feature and font path were
still present; this was a per-nameplate lifecycle/layout regression, not removal of the feature.

## Correction

- Write and validate the winner's name and appearance before registering the CHZZK marker.
- Patch every declared `UIFollowerName.SetText` overload and verify each Harmony postfix owner at
  startup.
- Ask TMP for `GetPreferredValues(nickname)` after a best-effort mesh refresh.
- Reject missing, non-finite, or implausibly small measurements and use a conservative string-width
  estimate. This catches the observed 2px stale layout value.
- Reposition both new and existing badge objects on every refresh.
- Run an immediate regeneration plus bounded 200ms retries for two seconds after marker sync or
  identity application. This covers nameplates instantiated on a later Unity layout frame without
  introducing permanent scene scans.

## Verification log

```text
[BUILD=rc26-nameplate-layout-lifecycle-fix]
[NAMEPLATE][PATCH-VERIFY] target=UIFollowerName.SetText(...), installed=True
CHZZK follower markers synced: ... ids=[12,35]
[NAMEPLATE][REFRESH] armed reason=marker-sync, duration=2s
[NAMEPLATE] badge created followerId=35, ... rawPreferredWidth=2, resolvedWidth=..., widthSource=estimated-stale-layout-fallback, badgeX=...
CHZZK village nameplate badge active: followerId=35, ... badge='Chzzk'
```

The old RC25 warning below must not appear during a new raffle identity application:

```text
CHZZK nameplate marker dropped: followerId=..., expectedName='new name', actualName='old name'
```
