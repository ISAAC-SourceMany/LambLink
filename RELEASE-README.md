# LambLink v1.0.0 — 배포용 GUI Installer

외부 사용자 배포 방식이 `ZIP + PowerShell`에서 **설치 EXE 하나**로 변경되었습니다.

## 사용자 설치 흐름

1. `LambLink-Setup-1.0.0.exe` 실행
2. 설치기가 Steam 라이브러리에서 Cult of the Lamb 자동 탐색
3. 아래 구성요소 다운로드 + SHA-256 검증 + 자동 설치
   - BepInEx 5.4.21 x64
   - COTL_API 0.3.4
   - COTL Korean Font Fix 4.2.1
   - LambLink Mod 1.0.0
   - LambLink Companion 1.0.0 (self-contained)
4. 바탕화면/시작 메뉴 Companion 바로가기 생성
5. Companion 실행 → CHZZK 로그인

사용자에게 AWS CLI, AWS SSO, .NET Runtime, DLL 수동 복사를 요구하지 않습니다.

## 설치 위치

- BepInEx / COTL_API / Mod / Font Fix: Cult of the Lamb 게임 폴더
- Companion: `%LOCALAPPDATA%\Programs\LambLink`
- 설치 로그: `%LOCALAPPDATA%\LambLink\installer.log`
- 설치 영수증/상태: `%LOCALAPPDATA%\LambLink\install-receipt.json`

## 배포자가 해야 할 것

1. 테스트된 `release-assets\COTL_KoreanFontFix\COTL_KoreanFontFix.dll`과 `koreanfont.bundle`을 준비합니다.
2. `build-distribution.ps1` 실행
3. 생성된 `CDN-UPLOAD` 폴더의 파일과 `SHA256SUMS.txt` 확인
   - BepInEx 5.4.21 공식 GitHub ZIP 다운로드/해시 계산
   - COTL_API 0.3.4 Thunderstore ZIP 다운로드/해시 계산
   - 자체 component ZIP 해시 계산
   - `release-hosting\installer-manifest-1.0.0.json` 생성
4. 아래 자체 파일을 CloudFront `/releases/` 경로에 업로드합니다.
   - `COTL-KoreanFontFix-4.2.1-v1.0.0.zip`
   - `LambLink-Mod-1.0.0.zip`
   - `LambLink-Companion-1.0.0-win-x64.zip`
   - `installer-manifest-1.0.0.json`
5. 클린 PC 설치 검증 후 사용자에게 `LambLink-Setup-1.0.0.exe` 하나만 배포합니다.

수익 창출 승인 전에는 실제 CHZZK 후원 수신만 미검증 상태입니다. 개발 후원으로 게임 적용,
대기열, 지역별 효과, 오버레이 표시를 검증했으며 실제 후원은 승인 후 동일 요청 ID 로그로
추가 확인합니다.

BepInEx와 COTL_API는 manifest에서 각각 공식 GitHub/Thunderstore URL을 사용합니다. 설치기는 모든 파일을 SHA-256으로 검증한 뒤 설치합니다.

## 진단

설치 과정은 `[DETECT]`, `[MANIFEST]`, `[DOWNLOAD]`, `[VERIFY]`, `[INSTALL]`, `[COMPLETE]`, `[ERROR]` 로그를 남깁니다. 외부 사용자 설치 문제가 생기면 `installer.log` 하나로 설치 단계부터 추적할 수 있습니다.

## RC5 검증 포인트

### CHZZK 마을 이름표

RC29는 RC28에서 검증한 방식대로 저장 이름은 순수 닉네임으로 유지하면서, 화면에 렌더링되는 기존
`UIFollowerName.nameText` TMP 문자열에 초록색 `Chzzk` 접두사를 인라인으로 합성합니다.
별도 GameObject를 만들지 않으므로 위치 계산·클리핑·생명주기 삭제 문제를 피합니다.

정상 로그 예:

```text
CHZZK follower markers synced: save=slot_0, count=1, ids=[12]
[NAMEPLATE][INLINE-APPLIED] followerId=12, vanillaName='유르밍', saveNameUntouched=true
```

### 설치기 응답 없음

ZIP 압축 해제와 실제 파일 설치/복사를 WinForms UI 스레드에서 분리했습니다.
긴 Companion 설치 중에도 창은 응답해야 하며 진행 막대는 marquee 애니메이션으로 계속 움직입니다.

설치 로그에는 각 병목 시간을 구분해 기록합니다.

```text
[EXTRACT] companion started
[EXTRACT] companion complete elapsed=...
[INSTALL] companion started mode=companion
[INSTALL] companion complete mode=companion, elapsed=...
```


## RC14 installer fixes
- Installer automatically stops a running LambLink Companion before replacing program files, preventing file-lock failures during update/reinstall.
- Companion is published as a self-contained single-file executable to drastically reduce ZIP entry count and antivirus/Defender extraction overhead.
- User data under `%LOCALAPPDATA%\LambLink` is not deleted by Companion program updates.
- Installation log records `[PROCESS]`, `[EXTRACT]`, and `[INSTALL]` timings for diagnosis.

### v1.0.0 verification
Companion must display `v1.0.0`. Staging installation must additionally display `STAGING TEST`,
use the `LambLink-Staging` program/data directories, and print a staging `[VIEWER PAGE]` URL. After CHZZK login, it must print the `[VIEWER PAGE]`
banner and support `viewer`, `viewer copy`, and `viewer open`. `status` must print the complete
`VIEWER_PAGE=https://.../?streamer=...` URL on the following line. `BepInEx/LogOutput.log` must contain
`[BUILD=v1.0.0-production]`,
`[DEPENDENCY] COTL_API=io.github.xhayper.COTL_API`,
`[RAFFLE][PATCH-VERIFY] ... installed=True`, `[DIAG][RUNTIME-HOST][INSTALLED]`,
`[DIAG][RUNTIME-HOST][FIRST-UPDATE]`, `[DIAG][UPDATE][FIRST]`, Mod
`[BRIDGE][RX][FRAME]`, `[BRIDGE][RX][QUEUED]`, `[BRIDGE][STATE][FALLBACK-TX-OK]`,
and `[BRIDGE][DISPATCH][BEGIN/END]`. Catalog completion is proven by
`[APPEARANCE][BUILD][END]` and `[APPEARANCE][TX] save=slot_...`.
If a call does not return, `[DIAG][WATCHDOG][NO-UPDATE]` or
`[DIAG][WATCHDOG][STALLED] stage=...` names the last active stage. Companion must contain
numbered `[Bridge][TX]`, `[Bridge][RX]`, and `[BRIDGE][DISPATCH]` pairs. Only after `status`
reports `GAME=True`, `GAME_SOCKET=True`, `GAME_READY=True`, `SYNC=READY`,
`SAVE=slot_...`, and `CATALOG>0` should automatic `!신도` raffle entry be tested. Automatic
raffle delivery is complete only after the Mod logs `[RAFFLE][ACK] ... accepted=true`.
When a round ends, Companion must log `[RAFFLE][ROUND-CLOSED-TX] ... sent=True` and the Mod
must log `[RAFFLE][ROUND-CLOSED]`. A no-participant/cancelled round must report
`allowRetry=True`; reopening the same pending recruit then starts another round. `status` should
report `OVERLAY=ready` while the OBS browser source is loaded and polling.

For startup-order recovery, select the generated `LambLink-Overlay.html` using OBS's
**Local file** option (run `overlay` in Companion for its full path). OBS and Companion
must run on the same PC. Existing HTTP URL sources require this one-time migration.
The local entry point stays alive before Companion starts, reloads failed/stalled child
pages, and hides stale content until a successful render heartbeat arrives. Verify OBS
first, Companion first, and Companion restart; the direct HTTP URL alone cannot recover
its own initial page-load failure. Local bootstrap updates require refreshing the OBS
source once; server page updates are picked up automatically.

For CHZZK follower nameplates, startup must log
`[NAMEPLATE][PATCH-VERIFY] ... installed=True`. After marker synchronization, the Mod must log
`[NAMEPLATE][REFRESH] armed`. Every decorated follower must log
`[NAMEPLATE][INLINE-APPLIED] ... saveNameUntouched=true`. `INLINE-APPLIED` is emitted only after
the Mod writes the rendered TMP string and reads the same value back. Any
`[NAMEPLATE][INLINE-FAILED]` is a blocking failure. The green `Chzzk` prefix and vanilla nickname
are rendered by the same TMP object, so no external badge position or clipping calculation remains.
After a raffle winner is applied, the Mod must log `[IDENTITY-COMMIT] armed`, then
`[IDENTITY-COMMIT] live follower acquired`, and finally `[IDENTITY-COMMIT] complete`. On a later
startup, Companion sends a marker only when the roster contains the same follower ID and the same
nickname. If the game was closed without saving, the rolled-back name mismatch is logged as
`[FOLLOWER-RECONCILE] stale/reused ID detected`, the stale mapping is removed, and no marker is
sent for that ID. The Mod never renames a loaded follower from marker data. For the existing saved
reference follower, synchronization should contain `ids=[12]`; approaching `유르밍` must produce
`[NAMEPLATE][INLINE-APPLIED] followerId=12, vanillaName='유르밍'` and visibly render a green
`Chzzk` prefix in the same name TMP object.
