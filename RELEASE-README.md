# ChzzkOfTheLamb v1.0.0 RC5 — GUI Installer

외부 사용자 배포 방식이 `ZIP + PowerShell`에서 **설치 EXE 하나**로 변경되었습니다.

## 사용자 설치 흐름

1. `ChzzkOfTheLamb-Setup-1.0.0.exe` 실행
2. 설치기가 Steam 라이브러리에서 Cult of the Lamb 자동 탐색
3. 아래 구성요소 다운로드 + SHA-256 검증 + 자동 설치
   - BepInEx 5.4.21 x64
   - COTL_API 0.3.4
   - COTL Korean Font Fix 4.2.1
   - ChzzkOfTheLamb Mod 1.0.0
   - ChzzkOfTheLamb Companion 1.0.0 (self-contained)
4. 바탕화면/시작 메뉴 Companion 바로가기 생성
5. Companion 실행 → CHZZK 로그인

사용자에게 AWS CLI, AWS SSO, .NET Runtime, DLL 수동 복사를 요구하지 않습니다.

## 설치 위치

- BepInEx / COTL_API / Mod / Font Fix: Cult of the Lamb 게임 폴더
- Companion: `%LOCALAPPDATA%\Programs\ChzzkOfTheLamb`
- 설치 로그: `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`
- 설치 영수증/상태: `%LOCALAPPDATA%\ChzzkOfTheLamb\install-receipt.json`

## 배포자가 해야 할 것

1. 테스트된 `release-assets\COTL_KoreanFontFix\COTL_KoreanFontFix.dll`과 `koreanfont.bundle`을 준비합니다.
2. `build-release.ps1` 실행
3. `prepare-installer-manifest.ps1` 실행
   - BepInEx 5.4.21 공식 GitHub ZIP 다운로드/해시 계산
   - COTL_API 0.3.4 Thunderstore ZIP 다운로드/해시 계산
   - 자체 component ZIP 해시 계산
   - `release-hosting\installer-manifest.json` 생성
4. 아래 자체 파일을 CloudFront `/releases/` 경로에 업로드합니다.
   - `COTL-KoreanFontFix-4.2.1.zip`
   - `ChzzkOfTheLamb-Mod-1.0.0.zip`
   - `ChzzkOfTheLamb-Companion-1.0.0-win-x64.zip`
   - `installer-manifest.json`
5. 사용자에게는 `ChzzkOfTheLamb-Setup-1.0.0.exe` 하나만 배포합니다.

BepInEx와 COTL_API는 manifest에서 각각 공식 GitHub/Thunderstore URL을 사용합니다. 설치기는 모든 파일을 SHA-256으로 검증한 뒤 설치합니다.

## 진단

설치 과정은 `[DETECT]`, `[MANIFEST]`, `[DOWNLOAD]`, `[VERIFY]`, `[INSTALL]`, `[COMPLETE]`, `[ERROR]` 로그를 남깁니다. 외부 사용자 설치 문제가 생기면 `installer.log` 하나로 설치 단계부터 추적할 수 있습니다.

## RC5 검증 포인트

### CHZZK 마을 이름표

RC5에서는 COTL의 `UIFollowerName.nameText` 값을 더 이상 `Chzzk 닉네임`으로 덮어쓰지 않습니다.
원래 닉네임 TMP 오브젝트의 자식으로 `CHZZK_PlatformBadge`를 별도 생성하여 초록색 `Chzzk`를 표시합니다.
따라서 저장 이름과 게임 원본 이름표는 항상 순수 닉네임만 유지합니다.

정상 로그 예:

```text
CHZZK follower markers synced: save=slot_0, count=1, ids=[12]
[NAMEPLATE] badge created followerId=12, name='유르밍', ...
CHZZK village nameplate badge active: followerId=12, vanillaName='유르밍', badge='Chzzk' ...
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


## RC10 installer fixes
- Installer automatically stops a running ChzzkOfTheLamb Companion before replacing program files, preventing file-lock failures during update/reinstall.
- Companion is published as a self-contained single-file executable to drastically reduce ZIP entry count and antivirus/Defender extraction overhead.
- User data under `%LOCALAPPDATA%\ChzzkOfTheLamb` is not deleted by Companion program updates.
- Installation log records `[PROCESS]`, `[EXTRACT]`, and `[INSTALL]` timings for diagnosis.

### RC10 verification
`BepInEx/LogOutput.log` must contain `CHZZK Companion Integration 1.0.0 loaded [BUILD=rc10]` and `[RAFFLE][PATCH] explicit raffle hook installation complete` before testing `!신도`.
