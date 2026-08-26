# RC30 validation record

## Acceptance contract

- Preserve the validated rc29 raffle, appearance catalog, viewer page, inline green `Chzzk` marker,
  and donation-effect behavior.
- Capture both Companion stdout and stderr with bounded retention.
- Correlate an actual CHZZK donation from realtime frame through game result using one request ID.
- Make every terminal state explicit, including send failure, game apply failure, result-send failure,
  and 15-second acknowledgement timeout.
- Create a local-only, best-effort-redacted support ZIP without tokens, viewer data, settings, saves,
  or automatic upload.
- Keep rc30 build and installer identities separate from rc29 deployment files.

## Evidence completed in the packaging environment

- Critical-source SHA-256 maps in `build-plugin.ps1`, `build-companion.ps1`, and
  `build-release.ps1` were recalculated and rechecked against the packaged files.
- Installer manifest JSON parsed successfully and all rc30 component URLs use immutable rc30 names.
- Static searches confirmed required markers for stdout/stderr logging, log rotation, support bundle,
  CHZZK donation frame receipt, request correlation, game apply stage, result TX, ACK, and timeout.
- Companion diagnostic markers are verified in the compiled Release DLL before publishing. The
  compressed single-file EXE is verified by existence and Windows file-version metadata; compressed
  bundle bytes are intentionally not treated as a searchable representation of managed strings.
- Static searches confirmed the support bundle excludes `settings.json`, viewer mapping/appearance
  contents, OAuth tokens, and game saves.
- The rc29 Mod was compared with rc30 changes: gameplay changes are restricted to donation diagnostic
  logging/result-send confirmation and removal of follower display names from donation detail strings.

## Environment limitation

The packaging environment does not provide .NET SDK, PowerShell, Unity Editor, or the Windows game.
Therefore compilation and runtime behavior are not claimed here. The Windows test machine must run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

The build scripts fail on non-zero restore/build/publish results, missing outputs, wrong file versions,
stale hashes, missing rc30 build tags, or missing diagnostic markers. Runtime validation then follows
`docs\RC30_LIVE_DONATION_DIAGNOSTICS_TEST.md`.

## Release status

Not ready for public deployment until the matched rc30 pair compiles on Windows and actual CHZZK
donations pass in both village and dungeon scenarios. Keep the already uploaded rc29 CDN objects intact
until those checks pass.
