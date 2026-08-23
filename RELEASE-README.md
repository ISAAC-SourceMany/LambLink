# ChzzkOfTheLamb v1.0.0 RC2

외부 배포용 릴리즈 후보입니다. 이 버전부터 COTL Korean Font Fix 4.2.1을 같은 설치 패키지에 포함하도록 릴리즈/설치 구조를 통합했습니다.

## 사용자에게 필요한 것

- Windows 10/11 64-bit
- Steam판 Cult of the Lamb
- BepInEx 5 및 COTL_API 설치
- CHZZK 계정

.NET 런타임은 Companion을 self-contained로 게시하므로 별도 설치할 필요가 없습니다.
AWS CLI, AWS 계정, AWS SSO, CHZZK Client Secret도 필요하지 않습니다.

## 함께 설치되는 구성요소

- ChzzkOfTheLamb 게임 Mod
- ChzzkOfTheLamb Companion
- COTL Korean Font Fix 4.2.1
  - `COTL_KoreanFontFix.dll`
  - `koreanfont.bundle`

한글 폰트 패치는 다음 위치에 자동 설치됩니다.

`Cult of the Lamb\BepInEx\plugins\COTL_KoreanFontFix\`

테스트된 4.2.1은 `koreanfont.bundle`에서 `KoreanRuntimeFont`를 로드하고 TMP 전역 fallback으로 등록하는 방식입니다.

## 설치

ZIP을 풀고 PowerShell에서 `Install-ChzzkOfTheLamb.ps1`을 실행합니다.
설치기는 Steam 라이브러리에서 Cult of the Lamb 경로를 자동 검색하고 Mod, 한글 폰트 패치, Companion을 함께 설치합니다.

설치 후 게임과 Companion을 실행합니다. Companion이 브라우저를 열면 CHZZK 연결을 승인합니다.

## OBS

브라우저 소스 하나만 추가합니다.

- URL: `http://127.0.0.1:17883/overlay`
- 권장 크기: 800x360

신도 추첨, 후원 이벤트, 버프/디버프가 같은 오버레이에 표시됩니다.

## 릴리즈 보안 구조

배포용 Companion은 AWS CLI/SSO를 호출하지 않습니다. CHZZK Client Secret은 AWS Secrets Manager에만 존재합니다. Companion OAuth는 Auth Gateway를 통해 처리되고 로컬 Companion에는 필요한 런타임 토큰만 전달됩니다.

## 현재 RC2 빌드 준비 사항

`build-release.ps1` 실행 전 아래 실제 테스트 바이너리를 `release-assets\COTL_KoreanFontFix\`에 넣어야 합니다.

- `COTL_KoreanFontFix.dll` (4.2.1)
- `koreanfont.bundle` (4.2.1에서 사용한 번들)

둘 중 하나라도 없으면 빌드 스크립트가 외부 배포 ZIP 생성을 중단합니다. 다른 폰트 파일이나 임의로 만든 번들로 대체하지 마세요.

## 주의

현재 설치기는 BepInEx와 COTL_API 자동 설치까지는 포함하지 않습니다. 이 두 의존성까지 자동 설치하게 만들면 일반 사용자는 별도 모드 사전 설치 없이 사용할 수 있습니다.
