# ChzzkOfTheLamb 1.0.0-rc28 배포 안내

## 빌드

Windows에서 .NET 8 SDK가 설치된 상태로 다음 명령 하나를 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-distribution.ps1
```

스크립트는 게임 Mod, self-contained Companion, GUI 설치기를 빌드하고 구성요소 ZIP과
`installer-manifest-1.0.0-rc28.json`의 SHA-256을 상호 검증합니다.

## 배포 파일

완료 후 `dist\ChzzkOfTheLamb-v1.0.0-rc28-distribution\` 아래에 두 폴더가 생성됩니다.

- `CDN-UPLOAD\`: 이 안의 네 파일을 CloudFront 원본의 `/releases/` 경로에 같은 이름으로 업로드합니다.
- `USER-DOWNLOAD\`: `ChzzkOfTheLamb-Setup-1.0.0-rc28.exe` 하나를 사용자에게 배포합니다.

설치기는 rc28 전용 매니페스트만 허용합니다. 이전 `installer-manifest.json`,
`ChzzkOfTheLamb-Mod-1.0.0.zip`, `ChzzkOfTheLamb-Companion-1.0.0-win-x64.zip`을
삭제할 필요는 없지만 rc28 설치기는 해당 파일을 참조하지 않습니다.

## 업로드 후 필수 확인

1. CDN의 `installer-manifest-1.0.0-rc28.json`을 열어 `release`가 `1.0.0-rc28`인지 확인합니다.
2. 기존 Companion과 게임을 종료한 클린 PC에서 설치 EXE를 실행합니다.
3. Companion 첫 줄이 `v1.0.0-rc28`인지 확인합니다.
4. BepInEx 로그에서 `[BUILD=rc28-inline-nameplate-safe-reconcile]`를 확인합니다.
5. 저장 슬롯·외형 카탈로그 동기화 후 자동 래플을 열고, 당첨 신도 이름 왼쪽에 초록색 `Chzzk`가 보이는지 확인합니다.
6. `SHA256SUMS.txt`는 보관하고, 빌드 산출물과 업로드 파일이 바뀌지 않았는지 대조할 때 사용합니다.

## 설치 실패 로그

- 설치기: `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`
- Companion: `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc28.log`
- 게임 Mod: `Cult of the Lamb\BepInEx\LogOutput.log`
