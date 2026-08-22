# devbridge10r 개발 요약

## 목표

Establish the three-project bridge foundation, local viewer service, appearance mapping, raffle flow, and donation command pipeline.

## 실제 변경 파일

- `A	.gitignore`
- `A	ChzzkOfTheLamb.sln`
- `A	HISTORY_RECONSTRUCTION.md`
- `A	NuGet.Config`
- `A	README.md`
- `A	aws/backend/app.py`
- `A	aws/backend/configuration.py`
- `A	aws/frontend/index.html`
- `A	aws/scripts/deploy-frontend.ps1`
- `A	aws/template.yaml`
- `A	build.ps1`
- `A	docs/ARCHITECTURE.md`
- `A	docs/AWS_VIEWER_APPEARANCE.md`
- `A	docs/CONFIGURATION_AND_IAM.md`
- `A	docs/COTL_1_5_25_APPEARANCE_REVERSE_ENGINEERING.md`
- `A	docs/DEVBRIDGE5_TEST.md`
- `A	docs/DEVBRIDGE9L_TEST.md`
- `A	docs/DEVBRIDGE9M_TEST.md`
- `A	docs/LOCAL_APPEARANCE_TEST.md`
- `A	docs/NEXT_STEPS.md`
- `A	docs/SETUP.md`
- `A	local-server/config_smoke_test.py`
- `A	local-server/configure-companion-local-aws.ps1`
- `A	local-server/configure-companion-local.ps1`
- `A	local-server/run-local-live-aws.ps1`
- `A	local-server/run-local-live.ps1`
- `A	local-server/run-local-mock.ps1`
- `A	local-server/server.py`
- `A	local-server/test-config-aws.ps1`
- `A	local-server/test-config-local.ps1`
- `A	src/ChzzkOfTheLamb.Companion/Appearance/AppearanceStore.cs`
- `A	src/ChzzkOfTheLamb.Companion/Chzzk/ChzzkApiClient.cs`
- `A	src/ChzzkOfTheLamb.Companion/Chzzk/ChzzkRealtimeClient.cs`
- `A	src/ChzzkOfTheLamb.Companion/Chzzk/LoopbackOAuth.cs`
- `A	src/ChzzkOfTheLamb.Companion/Chzzk/Models.cs`
- `A	src/ChzzkOfTheLamb.Companion/ChzzkOfTheLamb.Companion.csproj`
- `A	src/ChzzkOfTheLamb.Companion/Cloud/AppearanceApiClient.cs`
- `A	src/ChzzkOfTheLamb.Companion/Configuration/ChzzkCredentialProvider.cs`
- `A	src/ChzzkOfTheLamb.Companion/Configuration/CompanionSettings.cs`
- `A	src/ChzzkOfTheLamb.Companion/GameBridge/GameBridgeServer.cs`
- `A	src/ChzzkOfTheLamb.Companion/Overlay/RaffleOverlayServer.cs`
- `A	src/ChzzkOfTheLamb.Companion/Program.cs`
- `A	src/ChzzkOfTheLamb.Companion/Raffle/RaffleManager.cs`
- `A	src/ChzzkOfTheLamb.Companion/Rules/DonationRuleEngine.cs`
- `A	src/ChzzkOfTheLamb.Companion/Storage/ViewerFollowerRepository.cs`
- `A	src/ChzzkOfTheLamb.Mod/ChzzkOfTheLamb.Mod.csproj`
- `A	src/ChzzkOfTheLamb.Mod/Game/DonationEffectService.cs`
- `A	src/ChzzkOfTheLamb.Mod/Game/FollowerAppearanceService.cs`
- `A	src/ChzzkOfTheLamb.Mod/Game/FollowerNameplatePatch.cs`
- `A	src/ChzzkOfTheLamb.Mod/Game/FollowerService.cs`
- `A	src/ChzzkOfTheLamb.Mod/Game/GameSaveService.cs`
- `A	src/ChzzkOfTheLamb.Mod/Game/IndoctrinationRafflePatch.cs`
- `A	src/ChzzkOfTheLamb.Mod/Network/ModBridgeClient.cs`
- `A	src/ChzzkOfTheLamb.Mod/Plugin.cs`
- `A	src/ChzzkOfTheLamb.Protocol/ChzzkOfTheLamb.Protocol.csproj`
- `A	src/ChzzkOfTheLamb.Protocol/GameMessages.cs`

## 복원 근거

- 보존 소스: `ChzzkOfTheLamb-v0.1-devbridge10r.zip`
- 보존 시각 기준: `2026-08-22T17:30:58+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`feat: establish the local CHZZK game bridge`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
