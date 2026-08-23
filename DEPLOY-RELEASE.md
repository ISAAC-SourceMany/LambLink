# v1.0.0 RC1 배포자 체크리스트

이 문서는 스트리머 사용자에게 ZIP을 전달하기 전에 개발자/배포자가 한 번 수행하는 작업입니다.
일반 사용자는 AWS 계정이나 AWS SSO를 사용하지 않습니다.

## 1. CHZZK 개발자 콘솔

`COTL Companion` 앱의 Redirect URI를 아래 production callback으로 등록합니다.

`https://y0eblkdmu5.execute-api.ap-northeast-2.amazonaws.com/auth/companion/callback`

기존 로컬 개발용 callback이 필요하면 개발자 콘솔에서 복수 Redirect URI를 지원하는 범위 내에서 유지합니다.

## 2. AWS backend 배포

이번 RC1 backend에는 streamer Companion용 OAuth gateway가 추가되었습니다.

- `/auth/companion/start`
- `/auth/companion/callback`
- `/auth/companion/token`

Client Secret은 Lambda가 Secrets Manager에서 읽으며 사용자 PC로 배포되지 않습니다.
OAuth 완료 후 액세스 토큰은 DynamoDB의 2분짜리 one-time ticket으로 전달되고, Companion이 교환하는 즉시 삭제됩니다.

기존 SAM 스택을 `aws/template.yaml`로 다시 배포합니다. 이 단계에서만 개발자 AWS 자격증명이 필요합니다.

## 3. Release 빌드

Visual Studio Developer PowerShell 또는 dotnet SDK가 설치된 PowerShell에서:

```powershell
.\build-release.ps1
```

생성물:

`dist\ChzzkOfTheLamb-v1.0.0-win-x64.zip`

Release configuration에는 `RELEASE_DISTRIBUTION`이 정의됩니다. 이 빌드에서는:

- AWS CLI/SSO credential provider가 컴파일되지 않음
- CHZZK Client Secret이 Companion에 포함되지 않음
- `dev spawn`, `dev join`, `dev donation` 명령이 노출/실행되지 않음
- production Auth Gateway만 사용

## 4. 배포 전 필수 smoke test

1. AWS CLI 환경변수를 모두 지운 일반 Windows 계정에서 Companion 실행
2. 로그에 `[MODE] RELEASE / CHZZK LIVE` 확인
3. 로그에 `[CONFIG] AWS CLI/SSO: not used by distribution build` 확인
4. 브라우저 CHZZK OAuth 성공
5. `[AUTH] CHZZK connected:` 확인
6. 게임 Mod 연결 및 `area=BASE/DUNGEON` 전환 확인
7. `!신도` 추첨 확인
8. 실제 후원 이벤트 1회 또는 개발 환경에서 동일 commit Debug build로 효과 확인
9. OBS `http://127.0.0.1:17883/overlay` 확인
10. My Lamb catalog upload/appearance load 확인

## 5. RC1 제한

현재 설치 스크립트는 Cult of the Lamb 경로 자동 탐지, Mod/Companion 설치, 바탕화면 바로가기를 지원합니다.
다만 BepInEx 5와 COTL_API는 이미 설치되어 있어야 합니다. 두 의존성까지 자동 설치하도록 만든 뒤 정식 v1.0.0 설치기로 승격하는 것을 권장합니다.
