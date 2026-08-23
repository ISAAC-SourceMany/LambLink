# ChzzkOfTheLamb v1.0.0 RC1

외부 배포용 릴리즈 후보입니다.

## 사용자에게 필요한 것

- Windows 10/11 64-bit
- Steam판 Cult of the Lamb
- BepInEx 5 및 COTL_API 설치
- CHZZK 계정

.NET 런타임은 Companion을 self-contained로 게시하므로 별도 설치할 필요가 없습니다.
AWS CLI, AWS 계정, AWS SSO, CHZZK Client Secret도 필요하지 않습니다.

## 설치

ZIP을 풀고 PowerShell에서 `Install-ChzzkOfTheLamb.ps1`을 실행합니다.
설치기는 Steam 라이브러리에서 Cult of the Lamb 경로를 자동 검색하고 Mod/Companion 파일을 복사합니다.

설치 후 게임과 Companion을 실행합니다. Companion이 브라우저를 열면 CHZZK 연결을 승인합니다.

## OBS

브라우저 소스 하나만 추가합니다.

- URL: `http://127.0.0.1:17883/overlay`
- 권장 크기: 800x360

신도 추첨, 후원 이벤트, 버프/디버프가 같은 오버레이에 표시됩니다.

## 릴리즈 보안 구조

배포용 Companion은 AWS CLI/SSO를 호출하지 않습니다. CHZZK Client Secret은 AWS Secrets Manager에만 존재합니다. Companion OAuth는 Auth Gateway를 통해 처리되고 로컬 Companion에는 CHZZK 액세스 토큰만 전달됩니다.

## 주의

RC1 설치기는 BepInEx와 COTL_API 자동 설치까지는 포함하지 않습니다. 이 두 의존성 자동 설치를 추가한 뒤 정식 1.0.0 설치기로 승격하는 것을 권장합니다.
