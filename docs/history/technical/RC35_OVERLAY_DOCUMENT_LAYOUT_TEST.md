# RC35 오버레이 문서 갱신·후원 카드 배치 테스트

RC35는 게임 Mod와 Companion을 반드시 한 쌍으로 시험한다. 설치된 이전 Companion을 실행하면
`DONATION_RUNTIME_STATE` 동기화와 오버레이 타이머 정지가 작동하지 않는다.

## 빌드

압축을 깊은 다운로드 경로에 풀어도 정리 단계가 Windows 확장 경로를 사용해 처리된다. 그래도
외부 백신이나 동기화 프로그램이 파일을 잠그는 환경에서는 `C:\COTL\rc35`처럼 짧은 경로가 권장된다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

결과:

- `dist\rc35-plugin\LambLink.Mod.dll`
- `dist\rc35-plugin\LambLink.Protocol.dll`
- `dist\rc35-companion\LambLink.Companion.exe`

Companion 첫 화면은 `v1.0.0-rc35`, BepInEx 로그는
`[BUILD=rc35-overlay-document-handshake]`여야 한다.

## OBS 오버레이 문서 1회 갱신

rc34 이하 페이지는 Companion이 종료되어도 OBS 메모리에 남아 `/overlay/state`만 계속 요청할 수 있다.
그래서 rc35로 처음 전환할 때는 Companion이 출력하는 버전 포함 URL을 OBS 브라우저 소스에 한 번
입력하거나, 기존 브라우저 소스의 `현재 페이지의 캐시 새로고침`을 한 번 실행해야 한다.

```text
http://127.0.0.1:17883/overlay?v=rc35-overlay-document-v1
```

이후 버전부터는 상태 응답의 문서 버전이 달라지면 페이지가 스스로 새 문서를 다시 요청한다.
이전 문서가 살아 있으면 Companion이 아래 경고를 남긴다.

```text
[OVERLAY][STALE-DOCUMENT]
[OVERLAY][STALE-DOCUMENT][ACTION]
```

정상 문서가 로드되면 다음 순서가 나와야 한다.

```text
[OVERLAY][CLIENT] page loaded: ... document=rc35-overlay-document-v1
[OVERLAY][CLIENT-DOCUMENT] current=rc35-overlay-document-v1, versionHandshake=true
```

## 기본 확인

게임의 저장 파일에 들어간 뒤 Companion에서 `status`를 입력한다.

```text
GAME_READY=True
AREA=BASE 또는 DUNGEON
DONATION_GATE=READY
DONATION_READY=True
DONATION_QUEUE=0
OVERLAY_DOC=rc35-overlay-document-v1
OVERLAY_DOC_CURRENT=True
```

모드 로그에는 다음 기능 검증 행이 있어야 한다.

```text
[DONATION][STORY-HOOK][CAPABILITY]
[DONATION][GATE][CAPABILITY]
[DONATION][GATE][STATE] ready=True
```

## 개발 후원 테스트

`build-test-pair.ps1`이 만든 테스트 Companion에서 `[TEST TOOLS] RC35_TEST_TOOLS enabled`를
확인한 뒤 다음을 실행한다. 이 Companion은 CHZZK LIVE 로그인도 유지하지만 배포하면 안 된다.

```text
dev donation 1000
```

1. Base에서 실행하면 Base 규칙 효과가 적용되어야 한다.
2. Dungeon에서 실행하면 Dungeon 규칙 효과가 적용되어야 한다.
3. 로딩/장면 전환 중 실행하면 `[DONATION][QUEUE][ENQUEUED]` 뒤 즉시 적용되지 않아야 한다.
4. 전환이 끝나면 `[DONATION][QUEUE][DEQUEUED]`와 `[DONATION][APPLIED]`가 현재 지역 기준으로 나와야 한다.
5. Dungeon 대화·컷신 중 실행해도 같은 방식으로 대기해야 한다.

## 지속 버프 타이머 테스트

이동속도 또는 공격력 지속 효과가 걸린 상태에서 Dungeon 스토리 대화를 연다.

- 모드: `[DONATION][GATE][STATE] ... timersPaused=True`
- Companion: `[DONATION][GATE][RX] ... timersPaused=True`
- Overlay: 남은 시간이 줄지 않고 `일시정지`로 표시
- 대화 종료: `[OVERLAY][BUFF-TIMER][RESUMED]`
- 게임 효과와 Overlay 남은 시간이 다시 감소

## 후원 카드 크기

OBS 브라우저 소스를 1920×1080으로 설정한 뒤 `dev donation 3000`을 실행한다.

- 후원 카드의 최대 폭은 480px이어야 한다. 이전 720px 카드의 2/3이다.
- 카드의 가로 폭만 줄어들고 제목, 후원자, 이벤트명, 세로 패딩과 글자 크기는 기존과 같아야 한다.
- 카드 왼쪽과 위쪽은 각각 OBS 화면 경계에서 정확히 18px 떨어져야 한다.
- 래플 모집과 당첨자 카드는 기존 크기를 유지해야 한다.

## 버프 오버레이 배치

두 종류 이상의 지속 효과를 발생시킨다.

- 버프 영역의 시작점은 중앙 카드 내부가 아니라 OBS 화면 왼쪽에서 18px 떨어진 위치여야 한다.
- 최초 효과가 가장 왼쪽에 유지되고 새 종류는 그 오른쪽에 추가되어야 한다.
- 후원 카드가 보이는 동안 버프 행은 카드 아래 10px 간격으로 내려가며, 카드가 사라지면 화면 위쪽 18px로 돌아가야 한다.
- 동일 종류의 추가 후원은 기존 카드의 `대기 N`으로 표시된다.
- 창 너비가 부족할 때만 다음 줄로 줄바꿈되어야 한다.

Companion 시작 로그에서 아래 행을 확인한다. 이 행이 없으면 이전 Companion을 실행한 것이다.

```text
[OVERLAY][LAYOUT] donation=separate-fixed-layer,left=18px,top=18px,outerWidth=480px,buffs=separate-viewport-left-layer
```

후원 카드가 실제로 표시되면 브라우저가 계산한 좌표가 로그에 기록된다. 1920×1080 OBS 소스라면
`donation`의 앞 세 값이 반드시 `18,18,480`이어야 한다.

```text
[OVERLAY][CLIENT-LAYOUT] document=rc35-overlay-document-v1, phase=donation, viewport=1920x1080, donation=18,18,480,..., buffs=18,...
```

## 복합 버프 그룹 동기화

1. 공격력 단일 버프를 먼저 발생시킨다.
2. 첫 버프가 진행 중일 때 공격력과 이동속도가 함께 포함된 대형 또는 스페셜 후원을 발생시킨다.
3. 첫 공격력 버프가 끝나기 전에는 두 번째 후원의 공격력과 이동속도가 모두 적용되지 않아야 한다.
4. 첫 공격력 버프가 끝난 같은 순간에 두 번째 공격력과 이동속도가 함께 적용되어야 한다.
5. 두 카드의 남은 시간은 같은 값으로 시작해 함께 감소하고 함께 끝나야 한다.

Companion 로그에는 같은 `group` 번호와 같은 `startsIn`이 기록되어야 한다.

```text
[OVERLAY][BUFF-GROUP] group=2, ... members=2, startsIn=...
[OVERLAY][BUFF] queued group=2, key=speed, ... sharedStartsIn=...
[OVERLAY][BUFF] queued group=2, key=attack, ... sharedStartsIn=...
```

게임 로그에는 복합 효과가 별도 큐 호출 두 번이 아니라 하나의 그룹으로 기록되어야 한다.

```text
[DONATION][BUFF-GROUP] movement=..., attack=..., duration=..., schedule=buffGroup=queued; sharedStartIn=...
```

## 누적 후원 카드 FIFO

로딩이나 대화 중 `dev donation 3000`을 3회 입력한 뒤 플레이 가능한 상태로 돌아온다.

- 게임은 3건을 FIFO로 적용하고 Companion은 성공 ACK 3건을 모두 받아야 한다.
- 오버레이는 첫 후원 카드를 5초간 표시한 뒤 두 번째, 세 번째 카드를 각각 5초간 표시해야 한다.
- 현재 카드에 뒤따르는 후원이 있으면 `다음 후원 이벤트 N건 대기 중`이 표시되어야 한다.
- 로그는 각 sequence마다 `ENQUEUED`, `DISPLAY`, `COMPLETED`가 한 번씩 이어져야 한다.
- 표시 도중 래플이 열리면 현재 카드가 `PREEMPTED`로 대기열 맨 앞에 복귀하고, 래플 결과 표시가 끝난 뒤 남은 표시시간부터 재개되어야 한다.

```text
[OVERLAY][DONATION-QUEUE][ENQUEUED]
[OVERLAY][DONATION-QUEUE][DISPLAY]
[OVERLAY][DONATION-QUEUE][COMPLETED]
```

## 실패 시 수집

Companion에서 `support` 명령을 실행하고 생성된 ZIP과 `BepInEx\LogOutput.log`를 전달한다.
특히 `[DONATION][GATE]`, `[DONATION][STORY-HOOK]`, `[DONATION][QUEUE]`,
`[DONATION][BUFF-GROUP]`, `[DONATION][ACK-WAIT]`, `[OVERLAY][BUFF-GROUP]`,
`[OVERLAY][DONATION-QUEUE]`, `[OVERLAY][STALE-DOCUMENT]`, `[OVERLAY][CLIENT-DOCUMENT]`,
`[OVERLAY][CLIENT-LAYOUT]` 행을 보존한다.
