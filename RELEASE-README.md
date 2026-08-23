# ChzzkOfTheLamb v1.0.0 RC4 — GUI Installer

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
