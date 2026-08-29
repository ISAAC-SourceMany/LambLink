# LambLink RC35 개발 인수인계

작성일: 2026-08-28

인계 대상: 다른 Windows PC에서 Git clone/pull 후 테스트 및 개발 계속

기준 브랜치: `main`의 이 문서를 포함한 최신 원격 커밋

## 1. 현재 결론

RC35의 시청자별 신도 세대 이력, 부활 이력, 시청자 웹 외형 미리보기, 래플 최종 외형 조회,
CHZZK 후원 실시간 수신, 후원 exactly-once 복구, AWS staging 격리 코드는 구현되어 있다.
Release 빌드와 staging 인프라/정적 파일/구성요소 업로드도 완료했다.

실제 staging Companion OAuth는 스트리머 채널
`58349a33db2b4b76a2fa5d2b98afd9f9` (`ISAAC소스마니`)로 성공했다. CHZZK realtime에서
`CHAT`, `DONATION`, `SUBSCRIPTION` 세 구독 모두 SYSTEM 확인을 받고 READY가 됐다.

아직 게임을 실행하지 않아 마지막 상태는 다음과 같았다.

```text
GAME=False
GAME_SOCKET=False
GAME_READY=False
SYNC=DISCONNECTED
SAVE=unknown
CLOUD=connected
CATALOG=0
DONATION_OUTBOX=0
```

따라서 다음 작업의 첫 목표는 게임과 Mod를 연결해 실제 save catalog를 올리는 것이다.

## 2. 구현된 주요 변경

### 시청자 신도 세대와 부활

- 첫 신도는 시청자 닉네임만 사용하고 `1세`를 붙이지 않는다.
- 두 번째부터 `닉네임 2세`, `닉네임 3세` 형식으로 이름을 확정한다.
- 사망이 확정된 세대 뒤에는 다음 세대 설정/생성 권한이 열린다.
- 실종·감금·원정 등 생사가 확정되지 않은 상태는 사망으로 처리하지 않는다.
- 생성됨/사망함/부활함 이벤트와 이름, 외형, 생존 기간, 사망 원인을 세대별 이력으로 보존한다.
- 게임 부활식 후보에서 시청자 연동 신도를 제외하지 않는다.
- 과거 세대 부활은 이미 예약·설정된 다음 세대 생성을 취소하지 않는다. 따라서 부활 때문에 다음
  세대의 서버 저장 외형이 덮어써지거나 생성 권한이 소급 취소되면 버그다.
- 각 세대 외형은 immutable snapshot으로 남고, 과거 외형 복사는 현재 편집값만 바꾼다.

### 시청자 웹과 외형

- 서버에 마지막으로 저장된 외형을 래플 당첨 시 다시 조회해 적용한다.
- 이름과 외형을 기존 pending recruit ID에 함께 적용하며 새 recruit를 별도 생성하지 않는다.
- 외형/색상/변형 선택 항목과 조립된 정적 미리보기를 표시한다.
- 루트의 `Follower.atlas.bytes`, `Follower.skel.bytes`, `Follower.png`가 미리보기 원본이다.
- 대형 에셋은 콘텐츠 해시가 포함된 CloudFront 불변 경로로 업로드한다.

### CHZZK 후원과 복구

- 후원 realtime frame을 정규화하고 CHAT/DONATION/SUBSCRIPTION 구독 확인을 로그로 남긴다.
- Companion donation outbox와 Mod receipt를 사용해 재연결·재시작 시 중복 효과를 방지한다.
- `PENDING`, `APPLYING`, `APPLIED`, `UNCERTAIN` 복구 경로와 개인정보 제거 진단 로그를 추가했다.
- 실제 채팅 후원/영상 후원이 게임 효과로 정확히 한 번 적용되는지는 아직 미검증이다.

### staging 격리

- 운영과 별도인 CloudFormation 스택, API Gateway, Lambda, DynamoDB, S3, CloudFront, 토큰 Secret을 사용한다.
- Release Companion은 기본적으로 운영 주소에 고정된다.
- `COTL_STAGING_MODE=1`인 경우에만 staging endpoint override를 허용한다.
- staging 데이터는 `%LOCALAPPDATA%\LambLink-Staging`에 저장한다.
- staging 바로가기는 `CHZZK 시청자 외형 설정 페이지 (Staging).url`을 사용한다.
- 운영 스택과 운영 `/releases/` 경로는 이번 작업에서 변경하지 않았다.

## 3. AWS staging 현황

- 리전: `ap-northeast-2`
- 스택: `cotl-chzzk-staging`
- 시청자 웹: `https://d2wu3w1rbasd2q.cloudfront.net`
- API: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com`
- Companion callback: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com/auth/companion/callback`
- Viewer callback: `https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com/auth/chzzk/callback`
- 시청자 페이지:
  `https://d2wu3w1rbasd2q.cloudfront.net/?streamer=58349a33db2b4b76a2fa5d2b98afd9f9`
- 최종 staging 설치 매니페스트:
  `https://d2wu3w1rbasd2q.cloudfront.net/releases-staging/1.0.0-rc35-1d804f896a0c8d90/installer-manifest-1.0.0-rc35.json`

CHZZK staging 애플리케이션의 비밀이 아닌 Client ID:

- Companion: `669d9dfe-0bf6-4b63-ba80-e988ef6392b0`
- Viewer: `1326306b-38e7-4170-a323-5a1828c7d4de`

Client Secret은 AWS staging SSM/Secrets Manager에 이미 저장되어 있다. Git, 인수인계 문서,
채팅, 다른 PC의 평문 파일로 복사하지 않는다. Secret을 회전할 때만
`configure-staging-chzzk.ps1`을 다시 실행한다.

## 4. 다른 PC에서 재개

### 필수 준비

- Windows 10/11 x64
- Git
- PowerShell 7 권장
- .NET 8 SDK
- AWS CLI v2
- AWS SAM CLI
- Steam Cult of the Lamb과 BepInEx 5.4.21, COTL_API 0.3.4
- 이 프로젝트의 RC35 Mod

실제 게임 역컴파일 소스는 저작권 및 용량 때문에 저장소에 포함하지 않았다. 필요하면 별도로 확보해
기존 PC의 다음 참조 경로와 같은 구조로 둔다.

```text
C:\Users\user\Downloads\CultoftheLamb\Project\Assembly-CSharp
```

### Git과 빌드

```powershell
git clone https://github.com/ISAAC-SourceMany/LambLink.git
cd LambLink
git checkout main
git pull --ff-only origin main
dotnet build .\LambLink.sln -c Release
.\build-distribution.ps1
```

`dist`, `release-hosting`, `.aws-sam`, `aws/*.local.json`, AppData, OAuth 토큰과 로그는 Git에
포함되지 않는다. 다른 PC에서는 배포 산출물을 다시 빌드해야 한다.

### AWS 접속

이전 PC의 `cotl-staging` 프로필은 임시 루트 계정 로그인 세션이었다. 세션을 복사하지 말고,
다른 PC에서는 최소 권한 IAM 역할 또는 IAM Identity Center 프로필을 새로 만든다. 최소한 테스트 실행
스크립트에는 `cloudformation:DescribeStacks`가 필요하며, 재배포 시 SAM/CloudFormation/Lambda/
API Gateway/DynamoDB/S3/CloudFront/SSM/Secrets Manager 관련 staging 리소스 권한이 추가로 필요하다.

프로필 준비 후:

```powershell
aws sts get-caller-identity --profile cotl-staging
cd .\aws
.\scripts\run-staging-companion.ps1 -Profile cotl-staging
```

Companion이 브라우저를 열면 CHZZK 연결을 승인한다. 다음 로그를 확인한다.

```text
[MODE] RELEASE / CHZZK LIVE / STAGING
[AUTH] CHZZK connected: ISAAC소스마니 (58349a33db2b4b76a2fa5d2b98afd9f9)
[CLOUD] connected: https://3ug743cbxl.execute-api.ap-northeast-2.amazonaws.com
[CHZZK] realtime READY: CHAT / DONATION / SUBSCRIPTION confirmed.
```

### 게임 연결과 catalog 업로드

1. Companion을 켠 상태에서 Cult of the Lamb을 실행한다.
2. 실제 플레이할 세이브 슬롯을 로드한다.
3. Companion에서 `status`를 입력한다.
4. 다음 조건이 모두 충족될 때까지 웹/래플 테스트를 시작하지 않는다.

```text
GAME=True
GAME_SOCKET=True
GAME_READY=True
SYNC=READY
SAVE=slot_...
CATALOG>0
CATALOG_SAVE=slot_...
```

5. Companion 로그에서 `[CLOUD] catalog uploaded+verified`를 확인한다.
6. 위 시청자 페이지를 새로 열어 외형 목록이 표시되는지 확인한다.

## 5. 다음 테스트 순서

상세 체크리스트는 `PRE_RELEASE_TESTS_RC35_KO.md`를 따른다. 우선순위는 다음과 같다.

1. 게임 연결, save catalog 업로드, 허용 외형 수 확인
2. 별도 CHZZK 시청자 계정으로 Viewer OAuth 로그인
3. 외형/색상/변형 개별 미리보기와 조립 정적 미리보기 확인
4. 외형을 여러 번 저장하고 마지막 서버 값이 래플 당첨 시 적용되는지 확인
5. 첫 세대 이름, 사망, 다음 세대, 부활식, 이력/외형 복사를 실제 세이브에서 확인
6. 채팅 후원과 영상 후원을 실제로 수신해 게임 효과, outbox, receipt, 오버레이를 확인
7. Companion/게임 강제 종료와 재시작으로 exactly-once 복구 확인
8. 클린 PC 설치기와 덮어쓰기 설치, 사용자 데이터 보존 확인

실제 후원 테스트는 금전이 발생할 수 있으므로 스트리머가 금액과 시점을 직접 결정한다.

## 6. 배포 명령

인프라/프런트 갱신:

```powershell
cd .\aws
.\scripts\deploy-staging.ps1 -Profile cotl-staging
```

새로 빌드한 구성요소와 매니페스트만 staging 불변 경로에 업로드:

```powershell
.\scripts\deploy-release-staging.ps1 -Profile cotl-staging
```

운영 배포 경로에 덮어쓰지 않는다. staging 테스트가 끝나기 전에는 `cotl-prod` 또는 운영
CloudFront `/releases/`를 대상으로 배포하지 않는다.

## 7. 확인된 검증 결과

- `build-distribution.ps1` 전체 성공
- Mod/Companion Release 빌드 경고 0, 오류 0
- PowerShell staging 스크립트 구문 검사 성공
- `sam validate --lint` 성공
- staging API health, OAuth start redirect, CloudFront 프런트/미리보기 에셋 응답 확인
- 허용 CloudFront origin CORS 확인, wildcard CORS 제거
- Companion 실제 streamer OAuth 성공
- CHAT/DONATION/SUBSCRIPTION realtime 구독 SYSTEM 확인
- staging 전용 데이터 디렉터리와 `(Staging)` 바로가기 런타임 확인

## 8. 알려진 미완료·위험

- 실제 게임 연결 이후 기능은 아직 수동 검증 전이다.
- Viewer OAuth callback은 catalog가 올라간 뒤 최종 확인해야 한다.
- 실제 후원 이벤트의 게임 효과 적용은 미검증이다.
- 자동 테스트 프로젝트가 없어 핵심 게임 기능은 수동 회귀 테스트 의존도가 높다.
- 설치기와 Companion은 Authenticode 서명이 없어 SmartScreen/백신 경고가 날 수 있다.
- `Follower.png` 약 25 MB, `Follower.skel.bytes` 약 20 MB이므로 모바일 다운로드/메모리를 확인해야 한다.
- 현재 버전 문자열은 RC35지만 내용이 갱신되어 staging 매니페스트는 콘텐츠 해시 경로로 구분된다.
- staging 리소스는 테스트 완료 후 비용을 확인하고 유지 또는 제거한다.

## 9. 비밀·로컬 상태 체크

Git에 없어야 하는 항목:

- CHZZK Client Secret과 OAuth access/refresh token
- AWS access key, 임시 로그인 쿠키/세션
- `%LOCALAPPDATA%\LambLink*`의 설정·로그·outbox·이력
- `aws\staging-outputs.local.json`, `aws\staging-release.local.json`
- `dist`, `release-hosting`, `.aws-sam`
- 실제 Cult of the Lamb 역컴파일 소스

작업을 마칠 때는 다음을 실행한다.

```powershell
aws logout --profile cotl-staging
```
