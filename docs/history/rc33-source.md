# rc33-source 개발 요약

## 목표

Move the overlay layout left and synchronize grouped in-game buff state with the Companion UI.

## 실제 변경 파일

- `M	DEPLOY-INSTALLER.md`
- `M	DEPLOY-RELEASE.md`
- `R074	DISTRIBUTION-RC32.md	DISTRIBUTION-RC33.md`
- `M	README.md`
- `M	RELEASE-README.md`
- `M	SOURCE-MANIFEST.sha256`
- `R060	VALIDATION-RC32.md	VALIDATION-RC33.md`
- `M	build-companion.ps1`
- `M	build-distribution.ps1`
- `M	build-plugin.ps1`
- `M	build-release.ps1`
- `M	build-test-pair.ps1`
- `R053	docs/RC32_DONATION_OVERLAY_FIFO_TEST.md	docs/RC33_OVERLAY_AND_BUFF_GROUP_TEST.md`
- `M	installer/installer-manifest.template.json`
- `M	prepare-installer-manifest.ps1`
- `M	src/LambLink.Companion/LambLink.Companion.csproj`
- `M	src/LambLink.Companion/Overlay/RaffleOverlayServer.cs`
- `M	src/LambLink.Companion/Program.cs`
- `M	src/LambLink.Installer/LambLink.Installer.csproj`
- `M	src/LambLink.Installer/Program.cs`
- `M	src/LambLink.Mod/Game/DonationEffectService.cs`
- `M	src/LambLink.Mod/Game/DungeonDonationBuffs.cs`
- `M	src/LambLink.Mod/Plugin.cs`

## 복원 근거

- 보존 소스: `LambLink-v1.0.0-rc33-overlay-left-synchronized-buffs-source.zip`
- 보존 시각 기준: `2026-08-25T23:56:44+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`feat: synchronize buff groups in the overlay`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
