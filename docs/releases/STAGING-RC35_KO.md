# RC35 AWS staging 환경

배포일: 2026-08-28

리전: `ap-northeast-2`

CloudFormation 스택: `cotl-chzzk-staging`

## 배포 완료 항목

- 운영 `cotl-prod`와 분리된 API Gateway, Lambda, DynamoDB, S3, CloudFront 생성
- staging 전용 토큰 서명 Secret 자동 생성
- `/cotl/staging/...` 전용 CHZZK 설정 경로 적용
- 콘텐츠 해시 경로에 `Follower.atlas.bytes`, `Follower.skel.bytes`, `Follower.png` 업로드
- staging 프런트엔드 업로드 및 CloudFront 무효화 완료
- staging 전용 RC35 설치 매니페스트와 구성요소 ZIP 업로드
- API `/health` 200 확인
- 프런트 HTML, 세 미리보기 에셋, 설치 매니페스트와 구성요소 HEAD 200 확인
- CloudFront origin만 허용하는 CORS 확인
- 운영 `cotl-prod` 스택 미변경 확인

## staging 주소

- 시청자 웹: `https://d2wu3w1rbasd2q.cloudfront.net`
- API: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com`
- Companion OAuth 콜백: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com/auth/companion/callback`
- 시청자 OAuth 콜백: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com/auth/chzzk/callback`
- 설치 테스트 매니페스트: `https://d2wu3w1rbasd2q.cloudfront.net/releases-staging/1.0.0-rc35-1d804f896a0c8d90/installer-manifest-1.0.0-rc35.json`

## 사용자가 직접 해야 하는 CHZZK 작업

Client Secret은 채팅이나 저장소에 붙여넣지 않는다.

1. CHZZK 개발자센터에 staging Companion 애플리케이션을 별도로 만든다.
   - 로그인 리디렉션 URL: 위의 `Companion OAuth 콜백`
   - Scope: 채팅 메시지 조회, 후원 조회, 구독 조회, 유저 조회
2. staging 시청자 웹 애플리케이션을 별도로 만든다.
   - 로그인 리디렉션 URL: 위의 `시청자 OAuth 콜백`
   - Scope: 유저 조회
3. 두 애플리케이션이 승인·사용 중 상태가 되면 다음 명령을 사용자 PowerShell에서 실행한다. Client Secret은 화면에 표시되지 않는 보안 입력창에 입력한다.

```powershell
cd E:\ChzzkOfTheLamb\aws
.\scripts\configure-staging-chzzk.ps1 `
  -Profile cotl-staging `
  -CompanionClientId <STAGING_COMPANION_CLIENT_ID> `
  -ViewerClientId <STAGING_VIEWER_CLIENT_ID>
```

4. 작업이 끝나면 Codex에 완료 사실만 알린다. Client ID는 필요하면 전달할 수 있지만 Client Secret은 전달하지 않는다.

## staging Companion 실행

OAuth 설정 후 전용 실행 스크립트를 사용한다. 스크립트는 CloudFormation 출력에서 주소를
읽어 오며, 운영 Companion 데이터와 분리된 `%LOCALAPPDATA%\ChzzkOfTheLamb-Staging`을 사용한다.

```powershell
cd E:\ChzzkOfTheLamb\aws
.\scripts\run-staging-companion.ps1 -Profile cotl-staging
```

배포용 Companion은 기본적으로 계속 운영 주소에 고정된다. `COTL_STAGING_MODE=1`을 명시한
위 스크립트로 실행한 경우에만 staging API·웹 주소 override가 허용된다. 이 격리 기능이
포함되지 않은 이전 RC35 Companion으로 staging 테스트를 진행하면 안 된다.

Companion 로그인, catalog 업로드, 시청자 웹 로그인, 외형 저장과 래플 최종 외형 반영을 시험한다.

## staging 설치기 실행

운영 매니페스트를 변경하지 않고 로컬 설치기를 시험한다.

```powershell
$env:COTL_INSTALLER_MANIFEST_URL='https://d2wu3w1rbasd2q.cloudfront.net/releases-staging/1.0.0-rc35-1d804f896a0c8d90/installer-manifest-1.0.0-rc35.json'
& 'E:\ChzzkOfTheLamb\dist\ChzzkOfTheLamb-v1.0.0-rc35-distribution\USER-DOWNLOAD\ChzzkOfTheLamb-Setup-1.0.0-rc35.exe'
```

## 보안 및 종료

- 현재 `cotl-staging` AWS CLI 프로필은 임시 콘솔 로그인 세션이다.
- staging 작업이 끝나면 `aws logout --profile cotl-staging`을 실행한다.
- 이번 로그인은 루트 계정 세션이었으므로 반복 운영 전 최소 권한 IAM 역할 또는 IAM Identity Center 프로필을 만든다.
- 테스트가 모두 끝나면 CloudFront, Secrets Manager 등의 비용을 막기 위해 staging 스택과 버킷 객체를 제거한다.
