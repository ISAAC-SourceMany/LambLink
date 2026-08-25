# v1.0.0 RC30 진단 테스트·배포 체크리스트

## 1. Korean Font Fix 4.2.1 실제 바이너리 준비

테스트에 사용한 아래 파일을 그대로 준비합니다.

- `release-assets\COTL_KoreanFontFix\COTL_KoreanFontFix.dll`
- `release-assets\COTL_KoreanFontFix\koreanfont.bundle`

런타임 로그에서 검증된 설치 위치는 `BepInEx\plugins\COTL_KoreanFontFix\`입니다. 4.2.1은 번들의 `KoreanRuntimeFont`를 TMP fallback으로 등록합니다.

## 2. CHZZK / AWS 릴리즈 백엔드

- CHZZK 개발자 콘솔에 Companion production redirect URI 등록
- AWS Auth Gateway/backend 배포
- 사용자 배포본에는 AWS CLI/SSO/Client Secret을 포함하지 않음

## 3. 릴리즈 빌드

```powershell
.\build-distribution.ps1
```

폰트 DLL/번들이 없으면 스크립트가 즉시 실패하도록 되어 있습니다.

성공하면:

`dist\ChzzkOfTheLamb-v1.0.0-rc30-distribution.zip`

이 생성됩니다.

## 4. 클린 PC 검증

- Steam판 Cult of the Lamb + BepInEx 5 + COTL_API 환경
- 기존 ChzzkOfTheLamb/COTL_KoreanFontFix 폴더가 없는 상태
- 설치 스크립트 한 번으로 두 플러그인 폴더가 생성되는지 확인
- 게임 로그에 `COTL Korean Font Fix 4.2.1 loaded`가 찍히는지 확인
- `Loaded bundled Korean TMP font: KoreanRuntimeFont`가 찍히는지 확인
- CHZZK Companion이 AWS SSO 없이 로그인 플로우를 시작하는지 확인
- OBS overlay / raffle / 초록색 Chzzk 이름표 확인
- 마을·던전 실제 CHZZK 후원을 각각 테스트하고 동일 요청 ID의 RX/RULE/TX/APPLIED/ACK 확인
- 실패 시 `support` 명령으로 개인정보 제거 지원 ZIP 생성 확인

## 5. 정식 1.0.0 전 남은 권장 작업

BepInEx 5와 COTL_API까지 설치기에 포함하거나 안전한 자동 다운로드/검증 방식으로 처리하면, 사용자가 사전 모드 설치 없이 사용할 수 있습니다.
