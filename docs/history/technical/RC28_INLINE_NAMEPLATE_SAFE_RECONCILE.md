# RC28 safe inline CHZZK nameplate reconciliation

## Rendering contract

Persistent game data remains the plain viewer nickname. Only the visible
`UIFollowerName.nameText.text` is decorated:

```text
FollowerInfo.Name = 유르밍
UIFollowerName.nameText.text = <color=#00C471>Chzzk</color> 유르밍
```

The prefix uses the same TMP object, canvas, font, mask, sorting order, and transform as the
vanilla name. No separate badge GameObject, preferred-width calculation, external anchor, or
manual mesh refresh is used. Every discovered `UIFollowerName.SetText` overload is Harmony-
postfixed, and the exact decorated TMP value is read back before `INLINE-APPLIED` is logged.

## Ownership and save safety

A follower ID alone does not prove ownership because COTL can reuse IDs and an unsaved raffle
result legitimately disappears when the game is closed. Companion therefore sends a marker only
when all of these match the loaded save:

- streamer
- save slot
- viewer mapping
- follower ID
- normalized follower nickname

If the follower is missing or its nickname differs, Companion deletes the stale mapping. The Mod
also drops a mismatched marker without changing `FollowerInfo.Name`. RC28 does not read old logs,
recreate deleted mappings, or repair a loaded follower name from marker data.

The recruit-to-live identity commit remains active only during the current raffle session. It
keeps the selected name and appearance stable while COTL finalizes the new follower, but all of
that pending state is memory-only and disappears when the game exits. A later unsaved rollback is
therefore reconciled safely rather than resurrected.

## Reference verification

The supplied RC26 evidence proves that saved follower ID 12 is named `유르밍` and is the only
roster-validated CHZZK marker. No new raffle is required for the nameplate test.

Required logs:

```text
[BUILD=rc28-inline-nameplate-safe-reconcile]
[NAMEPLATE][PATCH-VERIFY] target=UIFollowerName.SetText(...), installed=True
[FOLLOWER-NAMEPLATE] synced CHZZK markers to game: save=slot_0, count=1, ids=[12]
CHZZK follower markers synced: save=slot_0, count=1, ids=[12]
[NAMEPLATE][INLINE-APPLIED] followerId=12, vanillaName='유르밍', renderedPrefix='Chzzk', color=#00C471, saveNameUntouched=true
```

The following messages are forbidden in an RC28 binary:

```text
[FOLLOWER-MIGRATION][RC26-RESTORED]
[FOLLOWER-MARKER][IDENTITY-REPAIRED]
[NAMEPLATE][IDENTITY-REPAIRED]
```

If an unsaved result rolled back, the expected behavior is deletion rather than recovery:

```text
[FOLLOWER-RECONCILE] stale/reused ID detected from roster: ...
[FOLLOWER-RECONCILE] removed stale mapping: ... identity no longer matches loaded save
```
