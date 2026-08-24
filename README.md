# ChzzkOfTheLamb v1.0.0 RC25 source release

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

## RC25 matched test pair

The installed Companion is not updated by building only the game plugin. Build both RC25
components together:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

Close the installed Companion and run `dist\rc25-companion\ChzzkOfTheLamb.Companion.exe`.
Replace the two game DLLs with the files under `dist\rc25-plugin`. The Companion title must show
`v1.0.0-rc25`, and the game log must show
`[BUILD=rc25-cotl-api-runtime-dependency-build-fix]` before testing.

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
- Mirrors the full Companion console to `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc25.log`.
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

RC3 release packaging bundles COTL Korean Font Fix 4.2.1 (`COTL_KoreanFontFix.dll` + `koreanfont.bundle`) under `BepInEx\plugins\COTL_KoreanFontFix` through the installer. See `RELEASE-README.md` and `DEPLOY-RELEASE.md`.

## v1.0.0 RC5 Installer

외부 배포용 GUI Bootstrapper 프로젝트 `ChzzkOfTheLamb.Installer`가 추가되었습니다. 사용자는 Setup EXE 하나만 실행하며, 설치기가 Steam 게임 위치를 자동 탐색하고 BepInEx/COTL_API/한글 폰트 패치/Mod/Companion을 다운로드·SHA-256 검증·설치합니다. 설치 진단은 `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`에 기록됩니다.


## RC5 fixes

- Village CHZZK platform marker is rendered as a separate green TMP badge. The vanilla follower name text and saved `FollowerInfo.Name` stay untouched, so later COTL `SetText` refreshes cannot erase the badge.
- Installer ZIP extraction and recursive component installation run on worker tasks instead of the WinForms UI thread. Detailed `[EXTRACT]` / `[INSTALL] ... elapsed=` diagnostics were added so long Companion installs remain responsive and bottlenecks are visible.


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
