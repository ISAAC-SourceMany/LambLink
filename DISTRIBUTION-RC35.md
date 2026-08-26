# ChzzkOfTheLamb 1.0.0-rc35 진단 테스트·배포 안내

## 빌드

Windows에서 .NET 8 SDK가 설치된 상태로 다음 명령 하나를 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-distribution.ps1
```

스크립트는 게임 Mod, self-contained Companion, GUI 설치기를 빌드하고 구성요소 ZIP과
`installer-manifest-1.0.0-rc35.json`의 SHA-256을 상호 검증합니다.

## 배포 파일

완료 후 `dist\ChzzkOfTheLamb-v1.0.0-rc35-distribution\` 아래에 두 폴더가 생성됩니다.

- `CDN-UPLOAD\`: 이 안의 네 파일을 CloudFront 원본의 `/releases/` 경로에 같은 이름으로 업로드합니다.
- `USER-DOWNLOAD\`: `ChzzkOfTheLamb-Setup-1.0.0-rc35.exe` 하나를 사용자에게 배포합니다.

설치기는 rc35 전용 매니페스트만 허용합니다. rc35 후원 대기열 검증 전에는 CDN에 업로드하거나
rc29 파일을 교체하지 않습니다. 검증 후 rc35 설치기는 `installer-manifest-1.0.0-rc35.json`과
rc35 구성요소만 참조합니다.

## rc35 진단과 지원 로그 묶음

Companion은 표준 출력과 표준 오류를 모두 다음 로그에 저장합니다.

```text
%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc35.log
```

로그는 5 MiB마다 순환하며 `.1`부터 `.4`까지 최근 기록을 보존합니다. `support` 명령은
Companion·설치기·BepInEx 로그와 런타임 파일 해시를 개인정보 제거 후 바탕화면 ZIP으로
생성합니다. 이 ZIP은 자동 업로드되지 않으며 스트리머가 내용을 확인한 뒤 직접 전달합니다.

```text
support       지원 ZIP 생성
support open  마지막 지원 ZIP을 탐색기에서 선택
```

OAuth 토큰, 설정 파일, 시청자 매핑·외형 파일, 게임 세이브는 ZIP에 포함하지 않습니다.

## 실제 후원 추적 기준

동일한 `request=xxxxxxxx` 값을 따라 아래 단계가 모두 이어져야 합니다.

```text
[CHZZK] DONATION frame received
[DONATION][RX]
[DONATION][RULE]
[DONATION][TX][SENT]
게임: [DONATION][RX]
게임: [DONATION][APPLIED] 또는 [DONATION][FAILED]
[DONATION][ACK][SUCCESS] 또는 [DONATION][ACK][FAILED]
```

`[DONATION][TERMINAL][ACK-TIMEOUT]`은 전송 뒤 게임이 플레이 가능한 상태였던 시간이 15초를
넘었는데 결과를 받지 못했다는 뜻입니다. 로딩·대화 중에는 이 제한시간도 정지합니다.

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

1. CDN의 `installer-manifest-1.0.0-rc35.json`을 열어 `release`가 `1.0.0-rc35`인지 확인합니다.
2. 기존 Companion과 게임을 종료한 클린 PC에서 설치 EXE를 실행합니다.
3. Companion 첫 줄이 `v1.0.0-rc35`인지 확인합니다.
4. 로그인 후 `[VIEWER PAGE]` 배너, `viewer copy`, `viewer open`, 바탕화면 바로가기를 확인합니다.
5. `status` 다음 줄의 `VIEWER_PAGE=https://.../?streamer=...`가 로그인 채널 ID와 일치하는지 확인합니다.
6. BepInEx 로그에서 `[BUILD=rc35-overlay-document-handshake]`를 확인합니다.
7. 저장 슬롯·외형 카탈로그 동기화 후 자동 래플과 초록색 `Chzzk` 이름표를 재확인합니다.
8. `docs\RC35_OVERLAY_DOCUMENT_LAYOUT_TEST.md`에 따라 오버레이 문서 버전, 후원 카드 480px·좌측 상단 18px 고정, 화면 왼쪽 버프 배치,
   복합 공격력·이동속도 효과의 동시 시작, Base/Dungeon/장면 전환 및 후원 카드 FIFO를 시험합니다.
9. 실제 후원은 수익 창출 승인 후 별도 검증하며, 그 전 배포는 조건부임을 기록합니다.
10. 실패하거나 단계가 누락되면 즉시 `support`를 실행해 생성된 ZIP을 보관합니다.
11. `SHA256SUMS.txt`는 빌드 산출물과 업로드 파일 대조용으로 보관합니다.

## 로그

- 설치기: `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`
- Companion: `%LOCALAPPDATA%\ChzzkOfTheLamb\companion-rc35.log` 및 `.1`~`.4`
- 게임 Mod: `Cult of the Lamb\BepInEx\LogOutput.log`
