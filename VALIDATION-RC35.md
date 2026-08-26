# RC35 validation record

## Acceptance contract

- Preserve the validated rc29 raffle, appearance catalog, viewer page, inline green `Chzzk` marker,
  and donation-effect behavior.
- Queue donations through loading, scene transitions, pause, dialogue and cutscenes; resolve the
  authoritative area only when the event is safe to apply.
- Pause game and overlay duration clocks together and render overlay cards left-to-right.
- Anchor timed buff/debuff cards to the viewport's left edge, independent of the centered raffle card.
- Render the donation-event card from a separate fixed layer at left/top 18px and exactly 480px maximum
  outer width while preserving existing raffle dimensions and donation typography.
- Detect a legacy in-memory OBS page, expose its document version in `status`, and make current pages
  automatically reload when a later Companion reports a different overlay document version.
- Treat all timed effects produced by one donation as one group: wait for every participating effect
  lane, then start all members on the same gameplay-clock and overlay-clock boundary.
- Preserve every successful donation presentation in ACK arrival order; show one donation card at a
  time and resume a raffle-preempted card without discarding it.
- Capture both Companion stdout and stderr with bounded retention.
- Correlate an actual CHZZK donation from realtime frame through game result using one request ID.
- Make every terminal state explicit, including send failure, game apply failure, result-send failure,
  and a 15-second ready-gameplay acknowledgement timeout that pauses with the Mod gate.
- Create a local-only, best-effort-redacted support ZIP without tokens, viewer data, settings, saves,
  or automatic upload.
- Keep rc35 build and installer identities separate from rc29 deployment files.

## Evidence completed in the packaging environment

- A Windows PowerShell 5.1 test run compiled and verified the RC33 baseline Mod, then failed before Companion
  restore while recursively deleting a long `Protocol\obj` tree. The reported child path had already
  disappeared, matching PowerShell's partial-delete/path-length failure rather than a C# build error.
- All build entry points now enumerate only top-level project `bin`/`obj` targets. Cleanup first uses
  `Remove-Item -LiteralPath`; if PowerShell partially deletes the tree and throws, it verifies the
  target and retries the remainder through the Windows extended path prefix (`\\?\`). A remaining
  directory still fails the build, so cleanup errors are not silently swallowed.
- The supplied rc31 runtime log proves the Mod queued and applied three requests FIFO after the
  transition (`680ed5c5`, `94889e41`, `0e9d2553`). Companion received three successful results, so
  the observed omission was isolated to `ShowDonation` overwriting one presentation slot.
- Static control-flow review confirms RC35 preserves the FIFO implementation that removes the
  raffle-time discard return, appends every
  successful presentation with `AddLast`, advances with `RemoveFirst`, and requeues a raffle-
  preempted active card with `AddFirst` while preserving its remaining display duration.
- Static control-flow review confirms `QueueMoveAndAttack` invokes one `QueueGroup` operation. The
  shared start is the maximum tail time of all participating queues and both scheduled records use
  that exact start. The Companion overlay performs the same two-pass reservation before adding cards.
- The supplied screenshots show the 720px centered document at both Base and Dungeon, ruling out an
  area-specific state branch. Static execution-path review shows `/overlay` owns HTML/CSS/JS while the
  already-loaded page polls only `/overlay/state` every 200ms. Restarting Companion therefore updates
  state without navigating an OBS document that remains alive in memory. Existing `no-store` headers
  affect the next navigation but cannot replace that already-running document.
- Overlay markup now uses three sibling presentation layers: centered `#wrap` for raffle/results,
  fixed `#donationWrap` for donation presentation, and fixed `#buffs` for timed effects. The donation
  panel uses `width:100%` plus `box-sizing:border-box`, making 480px the outer card width rather than a
  content width before padding and borders.
- State requests carry `documentVersion=rc35-overlay-document-v1`; the server detects missing/mismatched
  clients and logs a one-time actionable warning. Current documents compare the version in every state
  response and navigate to a cache-busted versioned URL on mismatch.
- Browser code reports calculated layout bounds on phase/viewport changes. This provides direct target-
  environment evidence such as `phase=donation ... donation=18,18,480,...` instead of inferring layout
  solely from compiled strings.
- Companion startup emits exact `[OVERLAY][DOCUMENT]` and `[OVERLAY][LAYOUT]` markers, and both test and
  release build scripts reject compiled assemblies without document-handshake, independent-layer and
  client-layout-report markers.
- Critical-source SHA-256 maps in `build-plugin.ps1`, `build-companion.ps1`, and
  `build-release.ps1` were recalculated and rechecked against the packaged files.
- Installer manifest JSON parsed successfully and all rc35 component URLs use immutable rc35 names.
- Static searches confirmed required markers for stdout/stderr logging, log rotation, support bundle,
  CHZZK donation frame receipt, request correlation, game apply stage, result TX, ACK, and timeout.
- Companion diagnostic markers are verified in the compiled Release DLL before publishing. The
  compressed single-file EXE is verified by existence and Windows file-version metadata; compressed
  bundle bytes are intentionally not treated as a searchable representation of managed strings.
- Static searches confirmed the support bundle excludes `settings.json`, viewer mapping/appearance
  contents, OAuth tokens, and game saves.
- The previously tested pair was used as the behavioral baseline. Raffle, appearance, follower identity/nameplate,
  cloud, donation rules, donation FIFO, gameplay gating and paused clocks remain unchanged. RC35
  preserves the already-reviewed grouped game/overlay scheduling and changes only overlay document
  lifecycle diagnostics, donation/buff layer ownership, and matched version/build identities.

## Environment limitation

The packaging environment does not provide .NET SDK, PowerShell, Unity Editor, or the Windows game.
Therefore compilation and runtime behavior are not claimed here. The Windows test machine must run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

The build scripts fail on non-zero restore/build/publish results, missing outputs, wrong file versions,
stale hashes, missing rc35 build tags, or missing diagnostic markers. Runtime validation then follows
`docs\RC35_OVERLAY_DOCUMENT_LAYOUT_TEST.md`.

## Release status

Not ready for public deployment until the matched rc35 pair compiles on Windows and the layout plus
grouped-buff test passes in Dungeon. Actual CHZZK donation receipt remains a documented conditional
validation until revenue approval is available.
