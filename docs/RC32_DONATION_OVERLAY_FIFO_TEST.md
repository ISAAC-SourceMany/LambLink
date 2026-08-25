# RC32 후원 대기열·오버레이 FIFO·타이머 정지 테스트

RC32는 게임 Mod와 Companion을 반드시 한 쌍으로 시험한다. 설치된 이전 Companion을 실행하면
`DONATION_RUNTIME_STATE` 동기화와 오버레이 타이머 정지가 작동하지 않는다.

## 빌드

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

결과:

- `dist\rc32-plugin\ChzzkOfTheLamb.Mod.dll`
- `dist\rc32-plugin\ChzzkOfTheLamb.Protocol.dll`
- `dist\rc32-companion\ChzzkOfTheLamb.Companion.exe`

Companion 첫 화면은 `v1.0.0-rc32`, BepInEx 로그는
`[BUILD=rc32-donation-overlay-fifo]`여야 한다.

## 기본 확인

게임의 저장 파일에 들어간 뒤 Companion에서 `status`를 입력한다.

```text
GAME_READY=True
AREA=BASE 또는 DUNGEON
DONATION_GATE=READY
DONATION_READY=True
DONATION_QUEUE=0
```

모드 로그에는 다음 기능 검증 행이 있어야 한다.

```text
[DONATION][STORY-HOOK][CAPABILITY]
[DONATION][GATE][CAPABILITY]
[DONATION][GATE][STATE] ready=True
```

## 개발 후원 테스트

`build-test-pair.ps1`이 만든 테스트 Companion에서 `[TEST TOOLS] RC32_TEST_TOOLS enabled`를
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

## 오버레이 배치

두 종류 이상의 지속 효과를 발생시킨다. 최초 효과가 왼쪽에 유지되고 새 종류가 오른쪽에
추가되어야 한다. 동일 종류의 추가 후원은 기존 카드의 `대기 N`으로 표시된다.

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
`[DONATION][ACK-WAIT]`, `[OVERLAY][DONATION-QUEUE]` 행을 보존한다.
