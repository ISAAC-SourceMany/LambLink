# rc35-source 개발 요약

## 목표

Add a document-layout handshake so the live overlay becomes ready before synchronized updates.

## 실제 변경 파일

- `M	DEPLOY-INSTALLER.md`
- `M	DEPLOY-RELEASE.md`
- `R076	DISTRIBUTION-RC34.md	DISTRIBUTION-RC35.md`
- `M	README.md`
- `M	RELEASE-README.md`
- `M	SOURCE-MANIFEST.sha256`
- `R063	VALIDATION-RC34.md	VALIDATION-RC35.md`
- `M	build-companion.ps1`
- `M	build-distribution.ps1`
- `M	build-plugin.ps1`
- `M	build-release.ps1`
- `M	build-test-pair.ps1`
- `R072	docs/RC34_OVERLAY_LAYOUT_AND_BUFF_GROUP_TEST.md	docs/RC35_OVERLAY_DOCUMENT_LAYOUT_TEST.md`
- `M	installer/installer-manifest.template.json`
- `M	prepare-installer-manifest.ps1`
- `M	src/LambLink.Companion/LambLink.Companion.csproj`
- `M	src/LambLink.Companion/Overlay/RaffleOverlayServer.cs`
- `M	src/LambLink.Companion/Program.cs`
- `M	src/LambLink.Installer/LambLink.Installer.csproj`
- `M	src/LambLink.Installer/Program.cs`
- `M	src/LambLink.Mod/Plugin.cs`

## 복원 근거

- 보존 소스: `LambLink-v1.0.0-rc35.zip`
- 보존 시각 기준: `2026-08-26T01:01:24+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`fix: handshake the overlay document layout`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
