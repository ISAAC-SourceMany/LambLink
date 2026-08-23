# ChzzkOfTheLamb devbridge10a

This revision is based on direct inspection of the user-provided COTL 1.5.25.1049 `Assembly-CSharp.dll`. It replaces heuristic appearance option discovery with the actual vanilla data flow and adds local rendering of the game's own follower-form UI template for web thumbnails. See `docs/COTL_1_5_25_APPEARANCE_REVERSE_ENGINEERING.md`.

# ChzzkOfTheLamb devbridge7

Prototype CHZZK integration for Cult of the Lamb.

## What changed in devbridge5

- Production flow no longer creates a new follower for a raffle.
- Mod detects a recruit that **Cult of the Lamb already created** and requests a raffle.
- Companion binds the raffle to that recruit ID.
- Winner nickname + saved appearance are applied to the existing recruit with `APPLY_RECRUIT_IDENTITY`.
- Multiple game-created recruits are queued; one raffle runs at a time.
- `dev spawn` remains development-only.
- Added an AWS serverless viewer-appearance service and `My Lamb` static website prototype.
- Companion can upload the streamer's current game catalog and fetch the raffle winner's saved web appearance.

See:

- `docs/DEVBRIDGE5_TEST.md`
- `docs/AWS_VIEWER_APPEARANCE.md`

Current local stack remains:

- Companion: .NET 8
- Protocol: .NET Standard 2.0
- Mod: BepInEx 5 / .NET Standard 2.0
- AWS backend: API Gateway + Lambda Python 3.12 + DynamoDB
- Viewer web: S3 + CloudFront

## devbridge9c: local My Lamb server before AWS

Use `local-server/server.py` to validate the same viewer-appearance API contract locally before deploying to AWS. See `docs/LOCAL_APPEARANCE_TEST.md`.


## devbridge7: streamer/viewer OAuth credential split

Two CHZZK developer applications are now treated separately:

- `COTL Companion`: streamer desktop OAuth, using `CHZZK_CLIENT_ID` / `CHZZK_CLIENT_SECRET`.
- `CHZ Viewer Lamb`: viewer web OAuth, using `MYLAMB_CHZZK_CLIENT_ID` / `MYLAMB_CHZZK_CLIENT_SECRET`.

The local My Lamb server and future AWS Lambda backend never read the desktop Companion client secret. The Companion continues to authenticate to the appearance backend with its already-issued CHZZK access token; the backend verifies that token via `/users/me`.

## devbridge9c configuration split

- Added shared configuration provider used by both local My Lamb server and AWS Lambda backend.
- `local` provider reads environment variables.
- `aws` provider reads Client IDs from SSM and Client Secrets from Secrets Manager.
- Added `local-server/test-config-local.ps1` and `local-server/test-config-aws.ps1` so production-style AWS configuration can be tested from the developer PC before Lambda deployment.
- See `docs/CONFIGURATION_AND_IAM.md`.
\n\n## devbridge9c local integration test\n\nUse two PowerShell windows.\n\n1. Viewer web/API server:\n```powershell\n$env:AWS_PROFILE="cotl-dev"\n$env:AWS_DEFAULT_REGION="ap-northeast-2"\n.\\local-server\\run-local-live-aws.ps1\n```\n\n2. Companion (same window for configuration + EXE):\n```powershell\n.\\local-server\\configure-companion-local-aws.ps1\ncd .\\src\\ChzzkOfTheLamb.Companion\\bin\\Debug\\net8.0\n.\\ChzzkOfTheLamb.Companion.exe\n```\n\nExpected Companion status after loading a save:\n`MODE=CHZZK ... SAVE=slot_0 ... CLOUD=connected, CATALOG=27, CATALOG_SAVE=slot_0`\n\n`aws-cli` is a local-development credential provider only. The final distributed Companion must authenticate through the AWS auth gateway and must not receive the CHZZK client secret.\n

## devbridge9f
- Automatically re-queries the running save's follower appearance catalog every 30 seconds by default.
- Only uploads to My Lamb when the catalog fingerprint actually changes.
- Change detection includes form IDs, unlock state, variants, colors, special/modded flags, and display name.
- Configure with `Appearance.AutoRefreshCatalog` and `Appearance.RefreshIntervalSeconds` in `%LOCALAPPDATA%\ChzzkOfTheLamb\settings.json` (minimum effective interval: 10 seconds).


## devbridge9f: live viewer catalog refresh

- My Lamb viewer pages poll the streamer catalog every 15 seconds.
- When the Companion uploads a changed catalog, already-open viewer pages pick it up without a manual reload.
- Current form/variant/color selections are preserved when still valid.
- If a selected option disappears, the page safely falls back to an available option.
- Catalog fetches use no-store semantics; returning to the tab triggers an immediate refresh.


## devbridge9g: safer game boot
- The in-game mod no longer opens the Companion WebSocket while the Unity `Splash` scene is active.
- Bridge startup is deferred until COTL has transitioned out of Splash, preventing integration networking from participating in the fragile boot window.
- Mod version bumped to `0.1.5`; Companion/local server banners now identify `devbridge9g`.
- Appearance auto-refresh and viewer-page polling from 9e/9f are unchanged.


## devbridge9i: catalog publish acknowledgement hardening
- Local server rejects transient `save=unknown` or empty catalog PUTs with HTTP 409 instead of returning a misleading 200.
- Server logs the raw PUT contract (`rawSave/rawForms/rawAllowed` and JSON keys) before normalization.
- Companion verifies the server acknowledgement matches the exact save/form/allowed counts it sent.
- Valid catalog uploads retry up to 3 times and only log `uploaded+verified` after the server confirms the same data.


## devbridge9i
- Fixed local My Lamb catalog uploads being parsed as `{}` when .NET sent a chunked HTTP request body.
- Companion now sends catalog JSON with an explicit Content-Length.
- Local server also supports chunked request bodies and logs request framing/byte count.


## devbridge9l: viewer appearance controls
- Viewer UI labels are fully Korean: `형상`, `종류`, `색상`.
- Known vanilla form IDs are displayed with Korean names; unknown/DLC/modded IDs fall back to the runtime name.
- Runtime catalog discovery now scans SkinAndData members for variation/color/palette collections and count fields, so selectable `SkinVariation` / `SkinColour` indexes can be published instead of only `게임 기본값`.
- Numeric runtime IDs are shown as friendly `종류 N`, `색상 N` labels while the original IDs are still saved and applied to the game.
- Catalog discovery log now includes variant/color coverage counts for one-pass diagnostics.

## devbridge9x preview fix
- Fixed the vanilla `FollowerFormItemTemplate` renderer to locate the real `IndoctrinationFormItem` component in the cloned prefab hierarchy instead of assuming the serialized template component itself owns `Configure(SkinAndData)`.
- Added explicit `[PREVIEW RENDER] ... failed:` diagnostics for every early-exit path so exact rendering failures are no longer silent.
- Bumped mod to 0.1.11 and Companion/local-server labels to devbridge9x.


## devbridge9x UI patch
- Removed all follower/example image preview presentation from the viewer UI.
- Kept form, colour and variant selection behavior unchanged.
- Colour choices continue to use the real runtime colour values received from the game.
- Added a neutral checker swatch for the game-default colour instead of an example follower image.
- Improved selection states, count labels, accessibility state and mobile layout.


## devbridge9x variant labels
- 종류 선택에서 별도의 `기본` 타일을 제거했습니다.
- 각 형상의 실제 variantIds를 `종류 1`, `종류 2`, `종류 3` 순서로 표시합니다.
- 형상을 바꾸면 첫 번째 종류가 자동 선택됩니다.


## devbridge9x
Removed the remaining dead preview-render/diagnostic methods after the preview feature was removed. This fixes CS0103 references to deleted preview state fields while keeping form/color/variant selection behavior unchanged.


## devbridge9x AWS deployment fix
- Production catalog PUT now returns the same verified acknowledgement that Companion expects (`ok/saveId/forms/allowed`).
- Empty/transient catalogs are rejected with HTTP 409 instead of being persisted.
- CloudFormation now outputs `FrontendDistributionId`.
- Frontend deployment script can invalidate CloudFront after upload.


## devbridge10a viewer URL fix
- Normalizes `COTL_WEB_FRONTEND_URL` with `Trim()` and `TrimEnd('/')`.
- Viewer setup URLs are now emitted as `https://host/?streamer=<channelId>`.
- Companion/local-server version labels updated to devbridge10a.

## devbridge10a - 래플 시작 시점 변경

- 교화 가능한 `FollowerRecruit`가 단순히 생성/대기 중이라는 이유만으로 래플을 시작하지 않습니다.
- `Lamb.UI.UIManager.ShowIndoctrinationMenu`가 실제로 열리는 순간을 Harmony로 감지합니다.
- 즉, 스트리머가 신도와 상호작용하여 세뇌/교화 UI를 연 순간부터 30초 래플이 시작됩니다.
- 같은 신도 ID에 대해 메뉴가 중복 호출되어도 래플은 한 번만 열립니다.
- 기존 1초 `FollowerRecruit` 스캔은 래플 트리거가 아니라 종료/정리용으로만 남겨두었습니다.

## devbridge10b - OBS 래플 오버레이
- Companion이 `http://127.0.0.1:17883/overlay` 로 OBS 브라우저 소스를 제공합니다.
- 래플이 열릴 때만 표시되고 평상시에는 투명합니다.
- `!신도` 안내, 남은 시간, 참가자 수, 마지막 5초 강조를 표시합니다.
- 추첨 종료 후 당첨자를 5초간 표시하며, 참가자 없음/취소 상태도 잠깐 표시합니다.
- 카운트다운 기준은 Companion의 실제 래플 종료 시각입니다.


## devbridge10e - CHZZK CHAT 수신 수정

- CHZZK 세션 URL의 `auth` 쿼리로 이미 루트 namespace 인증이 완료되므로 Engine.IO open 직후 수동 `40` CONNECT를 보내지 않도록 수정했습니다.
- 기존 수동 `40`은 CHZZK 서버에서 별도의 인증 없는 root namespace 연결로 해석되어 `error: auth fail`을 발생시켰습니다.
- `40`/`41` Socket.IO control packet 진단 로그를 추가했습니다.
- CHAT/DONATION/SUBSCRIPTION 이벤트 파싱과 래플 참가 로직은 그대로 유지합니다.


## devbridge10f - CHZZK realtime supervisor

- Companion can be started before or after a broadcast.
- If CHZZK Session acquisition fails before a live is ready, it retries automatically with bounded backoff.
- Socket close, namespace disconnect/error, SYSTEM revoked/unsubscribed, missing subscription confirmations, or heartbeat timeout forces a fresh Session URL.
- Every fresh Session automatically re-subscribes CHAT / DONATION / SUBSCRIPTION.
- `realtime READY` is logged only after all three SYSTEM subscribed confirmations arrive.
- No AWS or game-mod redeploy is required for this change; Companion only.

## devbridge10g - 당첨 즉시 교화 UI 외형/이름 동기화

- 래플 당첨 직후 저장된 My Lamb 형상/색/종류를 `FollowerInfo`에 기록하면서 `SkinCharacter`까지 즉시 동기화합니다.
- 열려 있는 COTL 교화 UI의 Form/Colour/Variant 캐시를 새 값으로 갱신하고 `ApplyCachedSettings`/선택 표시 갱신을 호출합니다.
- 따라서 스트리머가 외형 항목을 한 번 더 선택하지 않아도 당첨 직후 화면의 신도 미리보기가 갱신되도록 합니다.
- 신도 이름은 `<color=#00C471>Chzzk</color> 시청자닉네임`으로 저장하여 CHZZK 출신 신도임을 표시합니다.
- 교화 화면의 이름 입력 필드도 당첨 직후 같은 값으로 갱신합니다.
- 적용 직후 form/color/variant + 실제 SkinName/SkinCharacter/SkinColour/SkinVariation 값을 진단 로그로 남깁니다.


## devbridge10h - CHZZK CHAT 수신 안정화

- dev10f/10g의 `pingInterval + pingTimeout` 기반 95초 무트래픽 강제 재연결을 제거했습니다.
- CHZZK 방송이 조용한 동안에도 정상 세션을 끊지 않습니다.
- 실제 WebSocket close/error, Socket.IO error, SYSTEM revoked/unsubscribed 때만 세션을 재생성합니다.
- WebSocket keepalive(20초)를 사용합니다.
- 실제 장애 후 재연결 대기를 1초로 단축했습니다.
- CHAT 로그에 세션별 CHAT 수와 전체 수신 패킷 수를 추가했습니다.


## devbridge10j - CHZZK Engine.IO v3 heartbeat fix

- CHZZK Session uses Socket.IO v1/v2 / Engine.IO v3 (`EIO=3`).
- Engine.IO v3 heartbeat direction is client ping (`2`) -> server pong (`3`).
- Previous raw websocket implementation incorrectly waited for a server ping (v4 behavior), allowing the socket to look open while event delivery became stale.
- The Companion now reads `pingInterval` and `pingTimeout` from the handshake, sends Engine.IO ping packets on schedule, requires pong responses, and only refreshes the session when the heartbeat actually fails.
- CHAT diagnostics keep session counters so missing messages can be compared with CHZZK chat.

## devbridge10k

- 실제 로드된 COTL 세이브의 `DataManager.Instance.Followers` + `Followers_Recruit` 목록을 Companion으로 5초마다 동기화합니다.
- `viewer-followers.json`에 매핑이 남아 있어도 해당 Follower ID가 현재 세이브에 실제로 없으면 stale 매핑을 자동 삭제해 그 시청자의 `!신도` 재참가를 허용합니다.
- 진단 로그: `[FOLLOWER-ROSTER]`, `[FOLLOWER-RECONCILE]`.
- 래플 당첨 외형을 `FollowerInfo`에 기록한 뒤 열린 교화 UI의 vanilla `OnShowStarted`/캐시 적용 경로를 다시 실행합니다.
- 3초 동안 0.2초 간격으로 외형 적용/UI 갱신을 재시도하고 `FollowerRecruit.CharacterSetupCallback`도 호출해 교화 완료 전 받침대의 실시간 프리뷰가 My Lamb 외형으로 수렴하도록 보강했습니다.
- Mod version: 0.1.16 / Companion: devbridge10k.


## devbridge10l
- FollowerInfo.Name에는 TMP rich-text를 저장하지 않고 CHZZK 시청자 닉네임 원문만 저장합니다.
- 교화 화면의 초록색 `Chzzk` 표시는 저장 이름과 분리된 시각 전용 UI 배지로 표시합니다.
- 배지는 기존 교화 이름 필드의 TMP 텍스트를 복제하여 게임이 이미 로드한 폰트/머티리얼을 재사용하고, richText를 비활성화합니다.
- Mod version: 0.1.18 / Companion: devbridge10l.


## devbridge10n

- Fixes false `already has a follower` rejections caused by COTL reusing follower IDs.
- Existing-viewer checks now validate both the mapped follower ID and the current save roster name.
- If an ID still exists but belongs to a different follower name, the stale mapping is removed and raffle re-entry is allowed.
- Legacy persisted names like `<color=#00C471>Chzzk</color> nickname` are normalized for compatibility.
- Adds diagnostics showing mapped ID, expected viewer nickname, and actual roster follower name when a reused ID is detected.
- Companion version: devbridge10n. Mod remains 0.1.18.


## devbridge10n - 레거시 CHZZK 이름 데이터 자동 정리

- 기존 게임 세이브의 신도/교화대기 신도 이름이 `<color=#00C471>Chzzk</color> 닉네임` 형식이면 로드된 실제 FollowerInfo.Name을 `닉네임`만 남도록 자동 정리합니다.
- 이 변경은 게임 메모리의 실제 세이브 상태에 적용되며 다음 COTL 기본 저장/자동저장 때 `slot_*.mp`에 영구 반영됩니다.
- Companion의 `%LOCALAPPDATA%\ChzzkOfTheLamb\viewer-followers.json` 안 `LastKnownNickname`도 시작 시 즉시 정리하고 파일을 다시 저장합니다.
- 마이그레이션은 세이브 슬롯별 1회 수행하며 실패하면 다음 roster poll에서 재시도합니다.
- 새 신도 이름에는 앞으로도 rich-text 태그를 저장하지 않습니다.

## devbridge10o - 마을 신도 이름표 CHZZK 플랫폼 표시

- 신도 실제 `FollowerInfo.Name`과 Companion `viewer-followers.json`에는 시청자 닉네임만 저장합니다.
- 교화/신도 생성 화면에는 `Chzzk` 배지를 넣지 않습니다.
- COTL의 실제 마을 이름표 클래스 `UIFollowerName.SetText`를 Harmony로 후킹해, CHZZK 시청자 신도일 때만 렌더링 텍스트를 `<color=#00C471>Chzzk</color> 닉네임` 형태로 장식합니다.
- 장식 후 vanilla `RegenerateLabels`/`FollowersNameManager` 경로를 사용해 이름표 아틀라스를 즉시 재생성하므로 마을에서 바로 보이도록 합니다.
- Companion은 현재 세이브의 viewer↔follower 매핑을 Mod에 동기화합니다. Follower ID가 재사용되었거나 현재 이름이 매핑 닉네임과 다르면 stale 매핑을 제거하고 해당 신도에 CHZZK 표시를 붙이지 않습니다.
- 기존 `<color=#00C471>Chzzk</color> 닉네임` 저장 데이터의 닉네임-only 마이그레이션은 계속 유지됩니다.
- Mod version: 0.1.19 / Companion: devbridge10o.


## devbridge10q
- Fixed CHZZK village nameplate refresh so it no longer calls `UIFollowerName.RegenerateLabels` / shared `FollowersNameManager` atlas regeneration. Repeated regeneration was amplifying `Required atlas size exceeds supported max (4096x4096)` and could make individual Hangul glyphs disappear.
- Nameplate decoration now updates the active TMP label only; vanilla COTL owns atlas rebuild timing.
- Companion suppresses duplicate `SyncChzzkFollowerMarkers` payloads when the marker set has not changed, eliminating repeated nameplate refresh churn.
- Mod version: 0.1.20; Companion version: devbridge10q.
- Note: if Korean glyphs are still missing, the separate `COTL Korean Font Fix` must also be repaired. A startup log of `Korean dynamic fallbacks: 0` means no Korean fallback font was successfully created, independent of CHZZK nameplate logic.


## devbridge10q
- Fixes CS0165 in Companion Program.cs by moving `lastNameplateSyncSignature` initialization before any event handler/local-function call sites that capture it.

## devbridge10r — donation event pools + end-to-end diagnostics

Donation tiers now resolve to concrete safe COTL effects instead of one broad random key per tier.

- 1,000–2,999 KRW: 5 small events
- 3,000–4,999 KRW: 6 medium events
- 5,000–9,999 KRW: 6 help-or-hinder events
- 10,000+ KRW: 6 special events

Only faith and follower satiation/starvation are mutated. Story progression, quests, doctrines,
DLC unlocks and other soft-lock-prone state are intentionally excluded.

Existing `settings.json` files that still contain `SMALL_RANDOM`, `MEDIUM_RANDOM`,
`HELP_OR_HINDER_RANDOM`, or `SPECIAL_RANDOM` remain compatible; the Companion expands those
legacy tier keys into the new concrete event pools at runtime.

The donation path now logs:
CHZZK receive -> rule/tier selection -> game bridge send -> Mod input -> Mod apply/failure
-> Mod result -> Companion result.

The OBS overlay can show donor nickname, amount and the selected event name for about 5 seconds.
An active follower raffle countdown is never replaced by the donation card.

Versions:
- Companion: v0.1-devbridge10r
- Mod: 0.1.21

## devbridge10s

Dungeon donations now branch from village donations. The dungeon pool uses only combat/runtime members verified in the supplied Cult Of The Lamb 1.5.25.1049 Assembly-CSharp.dll. See `docs/DEVBRIDGE10S_DUNGEON_DONATION.md`.

Versions: Companion `v0.1-devbridge10s`, Mod `0.1.22`.
