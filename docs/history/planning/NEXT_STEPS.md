# Next implementation steps

> Historical planning snapshot. This document describes the project state at the time it was written and is not the current roadmap.

## Milestone A — compile and live transport verification

- Build with the exact installed Cult of the Lamb/BepInEx assemblies.
- Fix any game-library symbol changes against the current Steam build.
- Verify CHZZK OAuth + Session framing live.
- Add token persistence/refresh and reconnect/resubscribe tests.

## Milestone B — follower lifecycle verification

Implemented path:

- `RecruitSpawnLocation`
- `CreateNewRecruit`
- best-effort `FollowerInfo.Name`
- best-effort `FollowerInfo.ID`
- Companion persistence

Still verify in-game:

- whether `FollowerInfo` is available immediately on the current build or only at a later indoctrination hook;
- whether changing `Name` before indoctrination survives save/reload;
- exact save-slot identifier field;
- follower death patch to update `IsAlive`, `DiedAt`, `DeathReason`.

## Milestone C — automatic Twitch-like raffle trigger

The Companion already supports `RAFFLE_REQUESTED` and auto-start settings. The remaining task is to identify the correct game lifecycle event that represents "a viewer follower raffle should open" without spawning an extra recruit or interfering with normal recruits.

## Milestone D — appearance fidelity

- Verify the exact collection/filter used by `UIAppearanceMenuController_Form`.
- Extract variant and color option IDs from the current game build.
- Distinguish vanilla/DLC/modded/special forms more accurately.
- Add local preview generation without redistributing game assets.

## Milestone E — streamer UI

Replace console controls with WPF/MVVM:

- CHZZK/game connection indicators
- raffle start/cancel/draw + participant count
- appearance catalog with allow/deny checkboxes
- donation tier editor
- event log
- Steam install detection + mod install/update

## Milestone F — viewer appearance web flow

- CHZZK viewer authentication
- streamer-specific allowed catalog
- form/variant/color picker
- save by `streamerChannelId + viewerChannelId`

## Milestone G — public distribution

- hosted OAuth auth gateway
- installer/updater
- OBS overlay endpoint
- signed release artifacts

## 2026-08-21 first Visual Studio compile fixes

Fixed the first 10 compiler errors found on a Windows development PC:

- `JsonElement` is a value type, so the OAuth token response no longer uses `??`.
- Socket.IO payload parsing now uses a string overload accepted by `JsonDocument.Parse`.
- `ModBridgeClient` explicitly uses `System.Threading.Tasks.Task` because Cult of the Lamb game assemblies also expose a `Task` type.
- `ClientWebSocket.ReceiveAsync` in the `netstandard2.0` mod uses `ArraySegment<byte>`.
- Cult-faith changes resolve `CultFaithManager.GetFaith` and its flair enum at runtime, avoiding a compile-time dependency on a game enum whose exact type/name varies by build.

Rebuild the whole solution after applying these fixes. Any remaining errors are expected to reveal the next set of game-version-specific API differences.

## devbridge3 runtime verification

This build improves two adapters verified against Cult of the Lamb 1.5.25.1049:

- Active save discovery: reflection-first, then a safe `slot_*.mp` fallback. Expected status after loading the current test save: `save=slot_0`.
- Follower appearance discovery: resolves `WorshipperData` by runtime type name, reads its `Characters` collection, and filters against `DataManager.FollowerSkinsUnlocked` / `FollowerSkinsBlacklist` where available.
- Companion game-status logging now prints only when state changes and automatically refreshes forms after a real save ID becomes available.

Test after rebuilding/replacing the Mod DLL:

1. Load a save and verify `status` reports `SAVE=slot_N` rather than `unknown`.
2. Run `refresh-forms`, then `forms`; the catalog should contain the currently selectable unlocked follower forms.
3. Run `dev spawn <name>` again and verify the returned record is stored under the resolved save slot.

## devbridge4 safety fixes (2026-08-21)
- Prevent SPAWN_FOLLOWER while any recruit is already pending/active in indoctrination.
- Identify only the recruit created by the current CreateNewRecruit call; never rename a guessed "newest" recruit.
- Use FollowerLocation.Base explicitly.
- Build follower-form catalog from DataManager.FollowerSkinsUnlocked and WorshipperData.GetSkinIndexFromName.
- Do not treat FollowerSkinsBlacklist as a manual appearance-selection blacklist.
- Report save=unknown outside an active cult scene instead of the game's menu/default SAVE_SLOT value.
