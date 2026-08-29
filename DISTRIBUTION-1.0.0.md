# LambLink 1.0.0 배포 안내

## 주요 변경

- 프로그램 이름과 실행 파일, 설치 경로, 플러그인 폴더, 사용자 데이터 경로를 `LambLink`로 통일했습니다.
- 기존 `%LOCALAPPDATA%\ChzzkOfTheLamb*` 데이터는 새 `LambLink*` 경로에 없는 파일만 자동 복사하며 기존 데이터는 덮어쓰지 않습니다.
- 기존 BepInEx 플러그인 내부 ID는 중복 로딩 방지를 위한 호환 식별자로 유지하고, 사용자에게 보이는 이름과 산출물은 모두 `LambLink`를 사용합니다.
- 스테이징과 운영 Companion의 설치·데이터·OAuth·웹 주소를 분리했습니다.
- 래플 당첨자 적용 시 외형 카탈로그 캐시와 변경 대상 이름표 갱신을 사용해 프레임 드랍을 줄였습니다.
- 다른 PC에서 실행해도 클라우드 신도 상태를 복구한 뒤 이름표와 히스토리를 동기화합니다.
- 시청자 웹에서 188개 형상 미리보기, 형상별 실제 종류 수, 신도 히스토리를 제공합니다.

## 정식 빌드

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-distribution.ps1
```

산출물:

- `dist\LambLink-v1.0.0-distribution.zip`
- `dist\LambLink-v1.0.0-distribution\USER-DOWNLOAD\LambLink-Setup-1.0.0.exe`
- `dist\LambLink-v1.0.0-distribution\CDN-UPLOAD\installer-manifest-1.0.0.json`

Windows 파일 버전은 RC39 다음 빌드임을 나타내기 위해 `1.0.0.40`을 사용합니다.

## 환경 구분

- 운영 설치: `%LOCALAPPDATA%\Programs\LambLink`, `%LOCALAPPDATA%\LambLink`
- 스테이징 설치: `%LOCALAPPDATA%\Programs\LambLink-Staging`, `%LOCALAPPDATA%\LambLink-Staging`
- 운영 Companion에는 `RELEASE / CHZZK LIVE`, 스테이징 Companion에는 `RELEASE / CHZZK LIVE / STAGING`이 표시됩니다.
- 두 Companion을 동시에 실행하지 않습니다.

## 운영 배포

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\aws\scripts\deploy-production.ps1 -Profile cotl-staging
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\aws\scripts\deploy-release-production.ps1 -Profile cotl-staging
```

첫 스크립트는 `cotl-prod` 백엔드와 시청자 웹을 갱신하고, 두 번째 스크립트는 동일한 1.0.0 산출물을 운영 `/releases/` 경로에 게시합니다. 프로필 이름은 로컬 인증 별칭일 뿐이며 스크립트가 운영 스택과 `EnvironmentName=prod`를 검증합니다.
