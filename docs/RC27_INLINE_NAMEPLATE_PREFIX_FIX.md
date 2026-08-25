# RC27 deterministic inline CHZZK nameplate prefix

## Why RC26 could still be invisible

RC26 proved marker ownership, patched the name refresh lifecycle, and corrected stale name-width
measurement. It still rendered `Chzzk` in a child TMP object positioned outside the vanilla name
rectangle. That child remained subject to the game's parent masks, canvas clipping, hierarchy
sorting, and later layout rebuilds. The code could therefore report an active badge object without
proving that pixels from that object reached the screen.

The supplied RC26 run also exposed a second failure. The game roster reported follower ID 35 with
the original name `히티아르`, so Companion treated the viewer mapping as stale and permanently
deleted it. It then synchronized only ID 12. A follower without a synchronized marker cannot be
decorated regardless of the rendering method.

## RC27 rendering contract

The persistent game data remains:

```text
FollowerInfo.Name = viewer nickname
```

Only the visible `UIFollowerName.nameText.text` becomes:

```text
<color=#00C471>Chzzk</color> viewer nickname
```

This uses the same TMP object, canvas, font, mask, visibility state, sorting order, and transform as
the vanilla name. It has no badge position, preferred-width, or external hierarchy dependency.
Every vanilla `SetText` overload remains Harmony-postfixed, so a normal game name refresh restores
the prefix from authoritative CHZZK marker state.

Viewer nicknames are escaped before entering TMP rich text. The Mod then reads the TMP `text`
property back. It emits `INLINE-APPLIED` only when the exact decorated value persisted; otherwise it
emits `INLINE-FAILED` with the actual value.

RC27 also changes identity ownership and finalization handling:

- Companion removes a mapping only when the follower ID no longer exists in the loaded save.
- A present ID with a different name is retained and synchronized for repair.
- The Mod rewrites the mapped nickname when vanilla recruit finalization has overwritten it.
- Marker synchronization immediately repairs every matching live/recruit roster entry, even when
  that follower's floating nameplate is currently off-screen.
- Winner name and appearance are enforced on both recruit and live `FollowerInfo` objects during
  their hand-off, for at most 180 seconds.
- Enforcement stops after the live follower name remains correct for three seconds.
- RC27 scans only `companion-rc26*.log` for the exact regression deletion record. It restores that
  mapping only when the log belongs to the currently authenticated streamer and the current roster
  still contains the follower ID. This repairs mappings such as ID 35 that RC26 already removed
  from `viewer-followers.json` before the corrected policy could run.

## Required log

```text
[BUILD=rc27-inline-nameplate-prefix-fix]
[NAMEPLATE][PATCH-VERIFY] target=UIFollowerName.SetText(...), installed=True
CHZZK follower markers synced: ... ids=[...]
[NAMEPLATE][REFRESH] armed reason=marker-sync, duration=2s
[NAMEPLATE][INLINE-APPLIED] followerId=..., vanillaName='...', renderedPrefix='Chzzk', color=#00C471, saveNameUntouched=true
[FOLLOWER-MIGRATION][RC26-RESTORED] viewer=... (...), followerId=..., save=...; live ID verified, Mod will repair name drift
[FOLLOWER-MARKER][IDENTITY-REPAIRED] followerId=..., source=live-roster, oldName='...', restoredName='...'
[IDENTITY-COMMIT] armed recruit=..., displayName='...', maxDuration=180s; enforcing through recruit-to-live transition
[IDENTITY-COMMIT] live follower acquired recruit=..., name='...'; confirming stability for 3s
[IDENTITY-COMMIT] complete recruit=..., name='...', attempts=..., liveStableSeconds=...
```

This version also disables an existing `CHZZK_PlatformBadge` child created by RC5-RC26, preventing
duplicate labels after an in-place DLL upgrade.
