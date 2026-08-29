# RC30 실제 후원 진단 테스트

## 목적

개발자용 `dev donation`이 아니라 실제 CHZZK 후원 이벤트가 Companion에 도착하고 게임 효과 및
결과 회신까지 완료되는지를 한 요청 ID로 검증합니다. rc30 검증이 끝나기 전에는 rc29 배포 파일을
교체하지 않습니다.

## 빌드

압축을 푼 최상위 폴더의 PowerShell에서 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-test-pair.ps1
```

결과:

```text
dist\rc30-plugin\LambLink.Mod.dll
dist\rc30-plugin\LambLink.Protocol.dll
dist\rc30-companion\LambLink.Companion.exe
```

설치된 Companion을 완전히 종료한 뒤 rc30 Companion EXE를 직접 실행하고, 게임 플러그인 두 DLL도
rc30 결과물로 교체합니다. 다음 두 식별자가 모두 있어야 합니다.

```text
LambLink for Cult of the Lamb - v1.0.0-rc30
[BUILD=rc30-support-diagnostics-live-donation]
```

## 테스트 전 상태

Companion에서 `status`를 실행합니다. 다음 조건을 모두 충족해야 후원 테스트를 시작합니다.

```text
GAME=True
GAME_SOCKET=True
GAME_READY=True
SYNC=READY
SAVE=slot_...
DONATION_PENDING=0
```

CHZZK 로그에는 `realtime READY: CHAT / DONATION / SUBSCRIPTION confirmed.`가 있어야 합니다.

## 실제 후원 테스트

1. 방송 중 마을에 있는 상태에서 최소 금액 후원을 1회 보냅니다.
2. 요청의 `request=xxxxxxxx`를 기록합니다.
3. 효과가 게임에 실제 적용되는지 확인합니다.
4. `DONATION_PENDING=0`으로 돌아오는지 확인합니다.
5. 던전 입장 후 최소 금액 후원을 1회 더 보내 동일하게 확인합니다.
6. 같은 테스트를 연속 2회 반복해 요청 ID가 섞이거나 ACK가 누락되지 않는지 확인합니다.

성공 경로:

```text
[CHZZK] DONATION frame received
[DONATION][RX] ... request=xxxxxxxx
[DONATION][RULE] ... request=xxxxxxxx
[DONATION][TX][SENT] ... request=xxxxxxxx
BepInEx: [DONATION][RX] ... request=xxxxxxxx
BepInEx: [DONATION][APPLIED] ... request=xxxxxxxx
BepInEx: [DONATION][RESULT-TX] ... request=xxxxxxxx, sent=True
[DONATION][ACK][SUCCESS] ... request=xxxxxxxx, matched=true
```

안전한 실패도 결과 ACK가 있어야 합니다.

```text
BepInEx: [DONATION][FAILED] ... stage=...
[DONATION][ACK][FAILED] ... matched=true
```

## 실패 판정

- `DONATION frame received` 없음: CHZZK 세션/구독/이벤트 전달 구간
- frame은 있으나 `[DONATION][RX]` 없음: 역직렬화 또는 이벤트 핸들러 구간
- `[TX][SENT]` 없음: 규칙 판정 또는 Companion→게임 브리지 구간
- 게임 `[DONATION][RX]` 없음: Mod 소켓 수신/Unity 큐 디스패치 구간
- 게임 `[FAILED]`: `stage`와 예외 형식으로 런타임 API 실패 위치 확인
- 게임 `[APPLIED]` 뒤 `[RESULT-TX] sent=False`: 게임→Companion 결과 전송 소켓 구간
- 게임 `[RESULT-TX] sent=True` 뒤 Companion `ACK` 없음: Companion 결과 수신/디스패치 구간
- `[ACK-TIMEOUT]`: 15초 동안 결과 미수신. 당시 `gameSocket`과 `sync` 확인
- `matched=false`: 이미 타임아웃된 늦은 응답이거나 알 수 없는 결과

## 지원 ZIP 생성

실패 직후 Companion에 다음을 입력합니다.

```text
support
```

바탕화면에 `LambLink-Support-...zip`이 만들어집니다. 자동 업로드는 없으며 압축 내부를
확인한 다음 전달합니다. ZIP에는 최근 Companion 순환 로그, 설치 로그, BepInEx 로그, 버전·파일
해시 및 연결 상태가 들어갑니다. 토큰, 게임 세이브, 시청자 매핑·외형 저장 파일은 포함하지 않습니다.
