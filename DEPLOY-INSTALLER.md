# RC28 Installer 배포 체크리스트

## 1. 로컬 빌드

```powershell
.\build-distribution.ps1
```

`prepare-installer-manifest.ps1`이 생성한 manifest에는 실제 SHA-256이 들어가므로 수동으로 임의 해시를 넣지 않습니다.

## 2. CDN에 올릴 파일

CloudFront 배포 원본 S3의 `releases/`에 다음 파일을 배치합니다.

- `installer-manifest-1.0.0-rc28.json`
- `COTL-KoreanFontFix-4.2.1.zip`
- `ChzzkOfTheLamb-Mod-1.0.0-rc28.zip`
- `ChzzkOfTheLamb-Companion-1.0.0-rc28-win-x64.zip`

설치 EXE는 사용자 다운로드 페이지에 별도로 올려도 됩니다.

## 3. manifest 기본 URL

Installer에는 아래 URL이 기본값으로 내장됩니다.

`https://d1gvw9ccym1qvn.cloudfront.net/releases/installer-manifest-1.0.0-rc28.json`

개발 테스트 시에는 환경변수로 manifest URL만 바꿀 수 있습니다.

```powershell
$env:COTL_INSTALLER_MANIFEST_URL='https://.../installer-manifest-1.0.0-rc28.json'
.\ChzzkOfTheLamb-Setup-1.0.0-rc28.exe
```

정식 사용자는 이 환경변수를 설정할 필요가 없습니다.
테스트 URL을 사용해도 manifest의 `release`가 `1.0.0-rc28`이 아니면 설치기가 중단됩니다.

## 4. 외부 PC 검증

- BepInEx가 없던 PC/게임 설치 상태에서 설치
- COTL_API가 없던 상태에서 설치
- 기존 BepInEx/COTL_API가 있는 상태에서 덮어쓰기 설치
- Steam이 C:, D:, E: 라이브러리에 각각 있는 경우 탐색
- 다운로드 중 인터넷 차단 시 안전한 실패
- SHA-256을 일부러 틀린 테스트 manifest로 검증 실패 확인
- Companion에 AWS CLI/SSO 요구가 없는지 확인
- 게임을 먼저/Companion을 먼저 실행해도 크래시 없는지 확인


## RC14 installer fixes
- Installer automatically stops a running ChzzkOfTheLamb Companion before replacing program files, preventing file-lock failures during update/reinstall.
- Companion is published as a self-contained single-file executable to drastically reduce ZIP entry count and antivirus/Defender extraction overhead.
- User data under `%LOCALAPPDATA%\ChzzkOfTheLamb` is not deleted by Companion program updates.
- Installation log records `[PROCESS]`, `[EXTRACT]`, and `[INSTALL]` timings for diagnosis.
