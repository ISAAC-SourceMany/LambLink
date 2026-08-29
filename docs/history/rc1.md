# rc1 개발 요약

## 목표

Add production CHZZK OAuth, release packaging, installation scripts, and deployable AWS viewer services.

## 실제 변경 파일

- `A	DEPLOY-RELEASE.md`
- `A	Install-LambLink.ps1`
- `M	README.md`
- `A	RELEASE-README.md`
- `M	aws/backend/app.py`
- `M	aws/template.yaml`
- `A	build-release.ps1`
- `A	docs/DEVELOPMENT_HISTORY_README.md`
- `D	local-server/config_smoke_test.py`
- `D	local-server/configure-companion-local-aws.ps1`
- `D	local-server/configure-companion-local.ps1`
- `D	local-server/run-local-live-aws.ps1`
- `D	local-server/run-local-live.ps1`
- `D	local-server/run-local-mock.ps1`
- `D	local-server/server.py`
- `D	local-server/test-config-aws.ps1`
- `D	local-server/test-config-local.ps1`
- `M	src/LambLink.Companion/Chzzk/ChzzkApiClient.cs`
- `A	src/LambLink.Companion/Chzzk/ProductionOAuth.cs`
- `M	src/LambLink.Companion/LambLink.Companion.csproj`
- `M	src/LambLink.Companion/Configuration/ChzzkCredentialProvider.cs`
- `M	src/LambLink.Companion/Program.cs`
- `M	src/LambLink.Mod/LambLink.Mod.csproj`
- `M	src/LambLink.Mod/Plugin.cs`

## 복원 근거

- 보존 소스: `LambLink-v1.0.0-rc1-source.zip`
- 보존 시각 기준: `2026-08-23T10:23:00+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`feat: prepare the first release candidate`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
