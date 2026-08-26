# ChzzkOfTheLamb v1.0.0 RC34 synchronized buff overlay source

외부 배포를 위한 release candidate 소스입니다.

- 사용자 안내: `RELEASE-README.md`
- 배포자 체크리스트: `DEPLOY-RELEASE.md`
- Release ZIP 생성: `build-release.ps1`
- 사용자 설치: `Install-ChzzkOfTheLamb.ps1`

Release Companion은 AWS CLI/SSO를 사용하지 않으며 CHZZK Client Secret을 포함하지 않습니다.
Streamer OAuth는 AWS Auth Gateway를 통해 처리합니다.

## Build fix 1

- Fully qualifies all Mod `Task`, `Task<bool>`, and `Task.Delay` references as
  `System.Threading.Tasks` to prevent collision with the game assembly's `Task` type.
- Stops every build script immediately when a `dotnet` command returns a non-zero exit code.
- Verifies expected DLL/EXE files exist before copying or packaging them.

## RC34 matched diagnostic test pair

The installed Companion is not updated by building only the game plugin. Build both RC34
components together:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

The cleanup stage supports long Windows extraction paths. It retries partially removed `bin`/`obj`
trees through the Windows extended path prefix and still fails explicitly if a directory remains.

Close the installed Companion and run `dist\rc34-companion\ChzzkOfTheLamb.Companion.exe`.
Replace the two game DLLs with the files under `dist\rc34-plugin`. The Companion title must show
`v1.0.0-rc34`, and the game log must show
`[BUILD=rc34-overlay-top-left-ltr]` before testing.

## RC34 loading/dialogue-safe donations and paused buff timers

- Queues donation commands while the game is loading, changing scenes, paused, or in a detected
  dialogue/cutscene lifecycle.
- Applies queued commands FIFO after gameplay resumes and reselects the effect from the authoritative
  apply-time area: dungeon uses dungeon rules; every other ready location uses base rules.
- Advances dungeon timed buffs only during safe gameplay frames.
- Sends `DONATION_RUNTIME_STATE` to Companion so the OBS overlay timer pauses and resumes with the game.
- Anchors the donation card to the OBS viewport's top-left with 18px margins and preserves its
  original typography and vertical padding.
- Anchors buff/debuff cards to the OBS viewport's left edge; new effect types are added to the right.
- Reduces only the donation-event card to a 480px maximum width (2/3 of the previous 720px), while
  keeping raffle and winner cards at their existing size.
- Schedules every timed effect from one donation as a group. A composite attack+movement donation
  waits until both lanes are available, then both gameplay effects and overlay timers start together.
- Pauses the Companion's 15-second acknowledgement budget while the Mod reports a blocked gate.
- Queues every successful donation card in Companion and displays cards one at a time in ACK arrival
  order instead of overwriting the currently visible card.
- Shows the number of donation cards still waiting; an active raffle preempts and then resumes the
  current donation card with its remaining display time.
- See `docs/RC34_OVERLAY_LAYOUT_AND_BUFF_GROUP_TEST.md`.

COTL_API 0.3.4 remains a hard BepInEx runtime dependency and load-order gate. Because its public
surface does not provide a stable loading/dialogue lifecycle contract for the target game build,
rc34 uses BepInEx/Harmony against verified runtime type/method pairs plus Unity scene/timeScale
signals. It logs every discovered capability and never performs a per-frame global object scan.

## Support diagnostics and live donation correlation retained

- Captures both `Console.Out` and `Console.Error` in `companion-rc34.log`.
- Rotates the Companion log at 5 MiB and retains four archives.
- Correlates CHZZK receipt, rule resolution, bridge send, Unity apply, and result ACK with one request ID.
- Reports an explicit ACK timeout after 15 seconds of ready gameplay; loading/dialogue time is excluded.
- Catches fire-and-forget donation exceptions, unhandled exceptions, and unobserved task exceptions.
- Adds `support` to create a local, best-effort-redacted ZIP. Nothing is uploaded automatically.
- Excludes OAuth tokens, settings, viewer mapping/appearance records, and game saves.
- See `docs/RC34_OVERLAY_LAYOUT_AND_BUFF_GROUP_TEST.md`.

## RC28 safe deterministic inline CHZZK prefix

- Replaces the clipped child-TMP badge with a presentation-only rich-text prefix in the exact TMP
  object that renders the vanilla follower name.
- Renders `<green Chzzk> nickname` without changing the persisted `FollowerInfo.Name`.
- Reads TMP's `text` property back immediately and logs `INLINE-FAILED` if the render write did not
  persist, so a claimed success now proves the visible text object contains the prefix.
- Disables any child badge left by RC5-RC26 to prevent duplicate labels.
- Keeps all `UIFollowerName.SetText` overload hooks and bounded lifecycle retries.
- Sends a marker only when the loaded roster contains both the mapped follower ID and the mapped
  viewer nickname. An ID-only match is never treated as ownership proof.
- Removes stale mappings after an unsaved raffle rollback or follower-ID reuse and allows that
  viewer to enter a later raffle again.
- Never renames a loaded follower from marker data and never reconstructs mappings from old logs.
- Enforces the winner name and appearance across the recruit-to-live `FollowerInfo` hand-off for up
  to 180 seconds and stops after the live identity remains stable for three seconds.
- The already saved ID 12 follower `유르밍` is the reference test target; no new raffle is needed
  to validate the floating-name prefix.
- See `docs/RC28_INLINE_NAMEPLATE_SAFE_RECONCILE.md`.

## RC26 CHZZK nameplate correction

- Applies the CHZZK marker only after the raffle winner's persistent follower name is written,
  preventing the visible nameplate from dropping a valid marker as a reused follower ID.
- Measures the exact nickname through TMP and rejects transient/stale widths such as the observed
  `2px` value; the separate green `Chzzk` badge is placed to the actual left edge of the name.
- Refreshes visible nameplates for a bounded two-second post-layout window and patches every
  `UIFollowerName.SetText` overload.
- Logs Harmony installation state and the raw/resolved width used for every newly created badge.
- See `docs/RC26_NAMEPLATE_LAYOUT_LIFECYCLE_FIX.md`.

## RC25 COTL_API build correction

- Removes the compile-time `COTL_API` NuGet reference that pulled
  `UnityEngine.Modules >= 2021.3.16` into the Mod and caused `NU1605` against the existing
  `UnityEngine.Modules 2019.4.40` game-library reference.
- Keeps COTL_API 0.3.4 as an installed runtime component and keeps the official GUID
  `io.github.xhayper.COTL_API` as a hard `BepInDependency`, preserving API-first plugin load order.
- The Mod does not reference any COTL_API assembly type, so no compile-time COTL_API reference is
  required.

## RC24 raffle lifecycle correction

- Installs COTL_API `0.3.4` at runtime and declares its official GUID
  `io.github.xhayper.COTL_API` as a hard BepInEx dependency, guaranteeing API-first load order.
- Verifies at runtime that the `ShowIndoctrinationMenu` Harmony prefix is owned by this Mod.
- Adds `RAFFLE_ROUND_CLOSED` so cancellation, no-participant completion, failed identity
  application, and successful identity application release/finalize the correct recruit guard.
- Prevents the RC23 failure where a recruit ID stayed in `_announcedRecruitIds` forever and every
  later menu open was logged as already handled/announced/pending.
- Reports OBS browser polling as `OVERLAY=ready` or `OVERLAY=not-polling` in `status`.
- See `docs/RC25_COTL_API_RUNTIME_DEPENDENCY_BUILD_FIX.md`.

## RC23 end-to-end readiness and delivery confirmation

- Keeps the independent `DontDestroyOnLoad` main-thread host and gives it ownership of graceful
  application/replacement shutdown so duplicate bridge loops cannot accumulate.
- Distinguishes socket connectivity from a status produced by the Unity runtime pump.
- Retries `GET_GAME_STATUS` every 2 seconds until the pump is proven active, then retries the
  active-save catalog/roster every 5 seconds until a non-empty catalog arrives.
- Keeps an automatic raffle request pending until Companion returns an application-level ACK;
  WebSocket write completion alone is no longer treated as delivery.
- See `docs/RC23_END_TO_END_ACK_SYNC.md`.

## Corrective dispatch and one-run diagnostics

- Dispatches WebSocket commands before all optional follower/UI maintenance.
- Sends an immediate `GAME_STATUS` from a thread-safe cache when `GET_GAME_STATUS` is decoded,
  then sends the authoritative Unity-main-thread snapshot.
- Correlates Companion TX, Mod RX, Mod queue/dispatch, Mod TX, and Companion RX with numbered logs.
- RC34 mirrors stdout and stderr to rotating `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc34.log` files.
- Runs a non-Unity watchdog that reports `NO-UPDATE` or the exact last main-thread stage after 5 seconds.
- Splits catalog generation into save, type, singleton, unlock, palette, individual form, sort, and TX stages.
- See `docs/RC21_DIAGNOSTIC_WATCHDOG_FALLBACK.md` for the single-run decision table.

## RC20 main-thread dispatcher fix

- Removes the recurring global `FindObjectsOfType<FollowerRecruit>()` scan that ran before state synchronization.
- Runs GAME_STATUS dispatch before optional raffle maintenance.
- Resolves the indoctrination recruit directly from the UI hook arguments before using a fallback list.
- Logs `[BRIDGE][RX-QUEUED]` on the Mod network thread so socket receipt and Unity-main-thread dispatch are independently visible.
- Preserves the RC18 automatic appearance policy and the RC19 minimal status snapshot.
- See `docs/RC20_MAIN_THREAD_DISPATCH_FIX.md` for the evidence and verification sequence.

## RC19 safe GAME_STATUS synchronization

- Keeps the mandatory handshake limited to the confirmed-safe `InGame`, save ID, and Mod version probes.
- Removes optional game-version and dungeon-area evaluation from the handshake path that gates every catalog/roster request.
- Adds Companion `[BRIDGE][STATE][TX-OK] GET_GAME_STATUS delivered to Mod socket` confirmation.
- Retains Mod `[TX-START]` / `[TX-OK]` and Companion `[RX]` logs so both directions are independently visible.
- Preserves automatic appearance unlock publishing from RC18.
- See `docs/RC19_GAME_STATUS_SYNC_FIX.md` for the evidence and exact verification sequence.


## External release packaging

RC34 release packaging bundles COTL Korean Font Fix 4.2.1 (`COTL_KoreanFontFix.dll` + `koreanfont.bundle`) under `BepInEx\plugins\COTL_KoreanFontFix` through the installer. Run `build-distribution.ps1`; see `DISTRIBUTION-RC34.md` and `DEPLOY-RELEASE.md`.

## v1.0.0 RC34 Installer

외부 배포용 GUI Bootstrapper 프로젝트 `ChzzkOfTheLamb.Installer`가 추가되었습니다. 사용자는 Setup EXE 하나만 실행하며, 설치기가 Steam 게임 위치를 자동 탐색하고 BepInEx/COTL_API/한글 폰트 패치/Mod/Companion을 다운로드·SHA-256 검증·설치합니다. 설치 진단은 `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`에 기록됩니다.


## RC29 viewer-page sharing retained in RC34

- The green CHZZK platform marker is composed into the existing visible TMP name string while the saved `FollowerInfo.Name` remains untouched. No separate badge GameObject, position calculation, or badge lifetime is involved.
- Installer ZIP extraction and recursive component installation run on worker tasks instead of the WinForms UI thread. Detailed `[EXTRACT]` / `[INSTALL] ... elapsed=` diagnostics were added so long Companion installs remain responsive and bottlenecks are visible.
- After CHZZK login, Companion prints a dedicated viewer-page banner, supports `viewer`, `viewer copy`, and `viewer open`, persists the URL, and refreshes a Windows desktop `.url` shortcut for the authenticated channel.
- Installer and CDN component names are pinned to `1.0.0-rc34`; the installer rejects a manifest whose `release` is not exactly `1.0.0-rc34`.


## RC14 installer fixes
- Installer automatically stops a running ChzzkOfTheLamb Companion before replacing program files, preventing file-lock failures during update/reinstall.
- Companion is published as a self-contained single-file executable to drastically reduce ZIP entry count and antivirus/Defender extraction overhead.
- User data under `%LOCALAPPDATA%\ChzzkOfTheLamb` is not deleted by Companion program updates.
- Installation log records `[PROCESS]`, `[EXTRACT]`, and `[INSTALL]` timings for diagnosis.

## RC14 bridge/raffle reliability fix

- Game Mod starts the localhost Companion bridge directly from plugin `Awake` instead of waiting for an active Unity scene name.
- Adds `[BRIDGE][START]`, `[BRIDGE][CONNECT]`, `[BRIDGE][CONNECTED]`, `[BRIDGE][DISCONNECTED]` diagnostics.
- Adds `[RAFFLE][HOOK]` diagnostics whenever `ShowIndoctrinationMenu` is intercepted, including bridge state and recruit resolution failures.
- All game mutations still execute from the Mod `Update` queue on Unity's main thread; only localhost networking starts earlier.


## RC14 raffle trigger hardening

- Harmony now patches **all** `Lamb.UI.UIManager.ShowIndoctrinationMenu` overloads instead of only the first reflected overload.
- Adds `[RAFFLE][PATCH]` startup diagnostics with resolved signatures.
- Adds a 200 ms edge-triggered runtime fallback that detects active `Follower Indoctrination Menu(Clone)` and requests the raffle only when the actual UI becomes visible.
- Fallback uses the existing single-pending-recruit resolution and does not open a raffle just because a recruit exists.
- Adds `[RAFFLE][FALLBACK]` open/close diagnostics.
- Fixes the nullable `args` warning path by normalizing to an empty array.

## RC18 automatic appearance unlock publishing

- Newly unlocked normal follower forms are automatically added to the viewer allow-list.
- Explicit `form deny <id>` choices are stored separately and never auto-enabled later.
- Explicit `form allow <id>` removes the saved denial and restores the form.
- Existing RC17/older allow-list data is migrated once without discarding prior choices.
- Companion logs `[APPEARANCE][AUTO-ALLOW]` when a new form becomes available to viewers.
- See `docs/RC18_AUTO_UNLOCK_APPEARANCE.md` for migration and verification details.

## RC17 state synchronization and reliable raffle delivery

- Adds a `[BUILD=rc20-main-thread-scan-fix]` startup fingerprint so installed Mod freshness is visible in `LogOutput.log` even though the public plugin version remains `1.0.0`.
- Companion requests `GAME_STATUS` immediately after the WebSocket opens instead of treating transport connection as completed application synchronization.
- Mod state sends report `TX-START`, `TX-OK`, and `TX-FAILED`, and failed initial synchronization retries.
- A valid save immediately triggers appearance catalog and follower roster requests.
- Automatic raffle requests remain queued until their WebSocket delivery succeeds.
- See `docs/RC17_STATE_SYNC_FIX.md` for the RC14/RC16 Companion comparison and expected logs.

## Earlier raffle trigger hardening history

- Stops relying on Harmony assembly scanning for the production raffle trigger. `IndoctrinationRafflePatch.Install()` now explicitly resolves and patches every `Lamb.UI.UIManager.ShowIndoctrinationMenu` overload.
- Also patches `Lamb.UI.UIAppearanceMenuController_Form.OnShowStarted()` as a second independent trigger tied to the actual indoctrination appearance screen.
- Runtime fallback now detects an active `UIAppearanceMenuController_Form` instance first and passes that instance into recruit resolution; exact GameObject-name lookup is only secondary.
- Startup diagnostics report every discovered and installed raffle hook, so a stale CDN/installer build is distinguishable immediately.

## RC14 raffle trigger correction

- Primary raffle trigger moved from UI visibility to the vanilla `FollowerRecruit` interaction lifecycle.
- Explicitly patches `FollowerRecruit.ContinueRecruit` and `FollowerRecruit.ContinueRecruitRoutine` when present.
- `ShowIndoctrinationMenu` / appearance-controller hooks remain secondary compatibility fallbacks only.
- Removed the RC10 global UI object scan from the normal `Update()` path.
- A recruit is marked announced only after the WebSocket raffle request is actually sent successfully; disconnected/send-failed triggers remain retryable.
- New diagnostics: `[RAFFLE][INTERACTION]`, `[RAFFLE][REQUEST]`, and `[RAFFLE][PATCH][PRIMARY]`.
