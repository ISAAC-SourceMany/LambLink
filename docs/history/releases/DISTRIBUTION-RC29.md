# ChzzkOfTheLamb 1.0.0-rc29 배포 안내

## 빌드

Windows에서 .NET 8 SDK가 설치된 상태로 다음 명령 하나를 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-distribution.ps1
```

스크립트는 게임 Mod, self-contained Companion, GUI 설치기를 빌드하고 구성요소 ZIP과
`installer-manifest-1.0.0-rc29.json`의 SHA-256을 상호 검증합니다.

## 배포 파일

완료 후 `dist\ChzzkOfTheLamb-v1.0.0-rc29-distribution\` 아래에 두 폴더가 생성됩니다.

- `CDN-UPLOAD\`: 이 안의 네 파일을 CloudFront 원본의 `/releases/` 경로에 같은 이름으로 업로드합니다.
- `USER-DOWNLOAD\`: `ChzzkOfTheLamb-Setup-1.0.0-rc29.exe` 하나를 사용자에게 배포합니다.

설치기는 rc29 전용 매니페스트만 허용합니다. 이전 버전 파일을 삭제할 필요는 없지만
rc29 설치기는 `installer-manifest-1.0.0-rc29.json`과 rc29 구성요소만 참조합니다.

## 시청자 페이지 공유

스트리머가 Companion에서 CHZZK 로그인을 마치면 시청자 페이지 주소가 안내 배너로 표시됩니다.

```text
viewer       주소 다시 보기
viewer copy  주소를 클립보드에 복사
viewer open  기본 브라우저로 열기
```

Companion은 `%LOCALAPPDATA%\ChzzkOfTheLamb\viewer-page-url.txt`와
`viewer-page.url`을 갱신하고, Windows 바탕화면에
`CHZZK 시청자 외형 설정 페이지.url` 바로가기를 생성합니다. 로그인한 CHZZK 계정이
바뀌면 다음 실행에서 해당 계정의 채널 ID로 파일과 바로가기를 덮어씁니다.

## 업로드 후 필수 확인

1. CDN의 `installer-manifest-1.0.0-rc29.json`을 열어 `release`가 `1.0.0-rc29`인지 확인합니다.
2. 기존 Companion과 게임을 종료한 클린 PC에서 설치 EXE를 실행합니다.
3. Companion 첫 줄이 `v1.0.0-rc29`인지 확인합니다.
4. 로그인 후 `[VIEWER PAGE]` 배너, `viewer copy`, `viewer open`, 바탕화면 바로가기를 확인합니다.
5. `status` 다음 줄의 `VIEWER_PAGE=https://.../?streamer=...`가 로그인 채널 ID와 일치하는지 확인합니다.
6. BepInEx 로그에서 `[BUILD=rc29-viewer-page-sharing]`를 확인합니다.
7. 저장 슬롯·외형 카탈로그 동기화 후 자동 래플과 초록색 `Chzzk` 이름표를 재확인합니다.
8. `SHA256SUMS.txt`는 빌드 산출물과 업로드 파일 대조용으로 보관합니다.

## 로그

- 설치기: `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`
- Companion: `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc29.log`
- 게임 Mod: `Cult of the Lamb\BepInEx\LogOutput.log`
