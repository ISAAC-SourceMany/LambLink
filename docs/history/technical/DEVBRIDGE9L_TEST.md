# devbridge9l test focus

## Runtime game-resource previews
After entering a save, the mod catalog log includes:

- `previewForms`: forms whose icon/portrait sprite was exported from the installed game
- `variantPreviews`: variant/type sprites exported from the installed game
- `paletteColors`: real game palette colors mapped to color IDs

If any group is zero, `[PREVIEW DIAG]` prints candidate member names/types from a sample `SkinAndData` object so the next patch can target the exact current game build without repeating blind tests.

The Companion sends preview PNGs only as part of the catalog payload. The local server decodes them to `local-server/preview-cache/` and publishes them under `/preview-assets/...`. The web page renders image grids for 형상 / 색 / 종류 and uses those runtime-exported resources where available.

Expected server log example:

`catalog stored+verified: ... forms=27, allowed=27, previewForms=27, variantPreviews=54, paletteColors=675`

## dev spawn duplicate guard
`dev spawn <nickname>` remains development-only. The mod now arms a per-follower guard after `CreateNewRecruit` returns. It continuously removes duplicate `FollowerRecruit` scene objects and duplicate `Followers_Recruit` records carrying the same persistent follower ID. When the primary indoctrination recruit disappears, any remaining same-ID `FollowerRecruit` is treated as stale and destroyed before it can open a second indoctrination UI.

Expected log:

- `[DEV SPAWN GUARD] armed followerId=...`
- if a duplicate exists: `[DEV SPAWN GUARD] duplicate scene recruit removed ...`
- after normal indoctrination completes: `[DEV SPAWN GUARD] completed followerId=...; stale recruit flow cleared.`
