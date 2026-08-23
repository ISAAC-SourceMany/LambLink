# ChzzkOfTheLamb v1.0.0 RC1 source release

외부 배포를 위한 release candidate 소스입니다.

- 사용자 안내: `RELEASE-README.md`
- 배포자 체크리스트: `DEPLOY-RELEASE.md`
- Release ZIP 생성: `build-release.ps1`
- 사용자 설치: `Install-ChzzkOfTheLamb.ps1`

Release Companion은 AWS CLI/SSO를 사용하지 않으며 CHZZK Client Secret을 포함하지 않습니다.
Streamer OAuth는 AWS Auth Gateway를 통해 처리합니다.


## External release packaging

RC3 release packaging bundles COTL Korean Font Fix 4.2.1 (`COTL_KoreanFontFix.dll` + `koreanfont.bundle`) under `BepInEx\plugins\COTL_KoreanFontFix` through the installer. See `RELEASE-README.md` and `DEPLOY-RELEASE.md`.

## v1.0.0 RC4 Installer

외부 배포용 GUI Bootstrapper 프로젝트 `ChzzkOfTheLamb.Installer`가 추가되었습니다. 사용자는 Setup EXE 하나만 실행하며, 설치기가 Steam 게임 위치를 자동 탐색하고 BepInEx/COTL_API/한글 폰트 패치/Mod/Companion을 다운로드·SHA-256 검증·설치합니다. 설치 진단은 `%LOCALAPPDATA%\ChzzkOfTheLamb\installer.log`에 기록됩니다.
