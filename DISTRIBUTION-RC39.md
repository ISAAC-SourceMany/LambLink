# LambLink 1.0.0-rc39 배포 안내

## 주요 변경

- 프로그램 이름과 실행 파일, 설치 경로, 플러그인 폴더, 사용자 데이터 경로를 `LambLink`로 통일했습니다.
- 기존 `%LOCALAPPDATA%\ChzzkOfTheLamb*` 데이터는 새 `LambLink*` 경로에 없는 파일만 자동 복사하며 기존 데이터는 덮어쓰지 않습니다.
- 기존 BepInEx 플러그인 내부 ID는 중복 로딩 방지를 위한 호환 식별자로 유지하고, 사용자에게 보이는 이름과 산출물은 모두 `LambLink`를 사용합니다.
- 스테이징 설치는 `%LOCALAPPDATA%\Programs\LambLink-Staging`에 Companion을 별도로 설치합니다.
- 스테이징 OAuth·신도·외형·로그는 `%LOCALAPPDATA%\LambLink-Staging`에만 저장합니다.
- 설치된 `companion-launch-profile.json`이 스테이징 API와 시청자 웹을 자동 선택합니다.
- 바탕 화면과 시작 메뉴에 `LambLink Companion (STAGING TEST)` 바로가기를 생성합니다.
- 설치기와 Companion 시작 화면에 `STAGING TEST` 또는 `PRODUCTION` 환경을 명시합니다.
- 래플 당첨자 적용 시 외형 카탈로그 캐시와 변경 대상 이름표 갱신을 사용해 프레임 드랍을 줄입니다.
- 다른 PC에서 실행해도 클라우드 신도 상태를 복구한 뒤 이름표와 히스토리를 동기화합니다.

## 빌드

```powershell
pwsh -NoProfile -File .\build-distribution.ps1
```

산출물:

- `dist\LambLink-v1.0.0-rc39-distribution.zip`
- `dist\LambLink-v1.0.0-rc39-distribution\USER-DOWNLOAD\LambLink-Setup-1.0.0-rc39.exe`
- `dist\LambLink-v1.0.0-rc39-distribution\CDN-UPLOAD\installer-manifest-1.0.0-rc39.json`

## 스테이징 테스트

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\aws\scripts\install-release-staging.ps1`을 실행합니다.
2. 설치기 상단에 `STAGING TEST · 운영과 분리된 테스트 환경`이 표시되는지 확인합니다.
3. 설치 후 `LambLink Companion (STAGING TEST)` 바로가기를 실행합니다.
4. 시청자 웹에서 188개 형상 미리보기가 표시되고, 실제 데이터에 따라 형상별 종류가 1~5개로 표시되는지 확인합니다. 이 중 종류가 3개인 형상은 122개입니다.
5. 형상·색·종류를 연속해서 선택할 때 목록 전체가 다시 로딩되지 않고 즉시 선택 상태와 큰 미리보기만 갱신되는지 확인합니다.
6. Companion에서 `[MODE] RELEASE / CHZZK LIVE / STAGING`을 확인합니다.
7. 로그 경로가 `%LOCALAPPDATA%\LambLink-Staging\companion-rc39.log`인지 확인합니다.
8. 시청자 웹이 스테이징 CloudFront 주소인지 확인합니다.
9. 래플·Chzzk 이름표·한글 이름·외형·신도 히스토리를 검증합니다.

운영 Companion은 `%LOCALAPPDATA%\Programs\LambLink`과
`%LOCALAPPDATA%\LambLink`을 계속 사용합니다. 두 Companion을 동시에 실행하지 않습니다.

## 스테이징 배포

```powershell
pwsh -NoProfile -File .\aws\scripts\deploy-release-staging.ps1 -Profile cotl-staging
```

운영 `/releases/` 경로와 GitHub Release에는 위 검증을 마친 동일한 RC39 산출물만 승격합니다.
