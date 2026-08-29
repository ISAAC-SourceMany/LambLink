# rc21-diagnostics 개발 요약

## 목표

Add one-run diagnostics, watchdog fallback behavior, and delivery tracing across both processes.

## 실제 변경 파일

- `M	README.md`
- `M	RELEASE-README.md`
- `M	SOURCE-MANIFEST.sha256`
- `M	build-companion.ps1`
- `M	build-plugin.ps1`
- `M	build-release.ps1`
- `M	build-test-pair.ps1`
- `A	docs/RC21_DIAGNOSTIC_WATCHDOG_FALLBACK.md`
- `M	src/LambLink.Companion/LambLink.Companion.csproj`
- `A	src/LambLink.Companion/Diagnostics/TeeTextWriter.cs`
- `M	src/LambLink.Companion/GameBridge/GameBridgeServer.cs`
- `M	src/LambLink.Companion/Program.cs`
- `M	src/LambLink.Mod/Game/FollowerAppearanceService.cs`
- `M	src/LambLink.Mod/Game/GameSaveService.cs`
- `M	src/LambLink.Mod/Network/ModBridgeClient.cs`
- `M	src/LambLink.Mod/Plugin.cs`
- `M	src/LambLink.Protocol/GameMessages.cs`

## 복원 근거

- 보존 소스: `LambLink-v1.0.0-rc21-diagnostic-watchdog-fallback-source.zip`
- 보존 시각 기준: `2026-08-24T12:36:48+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`feat: add corrective dispatch diagnostics`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
