# RC15 dev10z rebase

## Purpose

RC15 restores the proven dev10z indoctrination raffle implementation directly instead of reconstructing it from later RC patches.

## Source policy

- `src/LambLink.Mod/Game/IndoctrinationRafflePatch.cs` is byte-for-byte identical to dev10z.
- `src/LambLink.Mod/Plugin.cs` is copied from dev10z. Only `PluginVersion`, `BuildTag`, and the startup build-tag log differ.
- Production Companion authentication, cloud API, installer, hosting, font-patch, donation, appearance, and protocol sources remain based on RC14.
- No RC9-RC14 generic raffle trigger, in-flight state, or fallback hook is used by the game mod.

## Expected game log

When the indoctrination menu opens for a recruit while the Companion bridge is connected:

```text
LambLink 1.0.0 loaded [BUILD=rc15-dev10z-rebase]
CHZZK raffle requested at indoctrination start for game recruit <id> (source=<source>)
```

If the second line is absent, capture the complete BepInEx `LogOutput.log`. Do not test with an older DLL: confirm the RC15 build tag first.

## Clean build requirement

For the quickest game-mod test, run `build-plugin.ps1`. For the complete distribution, run `build-release.ps1`. Both scripts verify the reviewed critical-source hashes, delete all project `bin` and `obj` directories, compile, and reject a DLL that does not contain the RC15 build tag.
