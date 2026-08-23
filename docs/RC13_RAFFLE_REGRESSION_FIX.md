# RC13 raffle regression fix

The known-good dev10z source and RC11 were diffed directly.

Key regression found:

- dev10z: `IndoctrinationRafflePatch` had `[HarmonyPatch]`, `TargetMethod()`, and `Prefix(object[] __args)`, and was installed by `Harmony.CreateAndPatchAll(...)`.
- RC11: the `[HarmonyPatch]` class was replaced by a manually installed `Harmony.Patch(...)` implementation and multiple speculative interaction/UI hooks.

RC13 restores the known-good HarmonyX patch style for `Lamb.UI.UIManager.ShowIndoctrinationMenu` and keeps the later bridge/release/installer improvements.

Expected startup log:

`[RAFFLE][PATCH] [LEGACY-RESTORE] automatic Harmony target=Lamb.UI.UIManager.ShowIndoctrinationMenu(Follower,OriginalFollowerLookData)`

Expected when indoctrination starts:

`[RAFFLE][PATCH] [LEGACY-RESTORE] ShowIndoctrinationMenu Prefix fired; args=2`

Then:

`[RAFFLE][UI-FALLBACK] indoctrination UI signal...`
`[RAFFLE][REQUEST] sending recruit=...`
`CHZZK raffle requested ...`
