# COTL Korean Font Fix 4.3.0 Release

Cult of the Lamb에서 한글 문자열과 한글 직접 입력을 안정적으로 표시하기 위한 BepInEx/TextMeshPro 패치입니다.

## 4.3.0 Release 변경점

이 버전은 4.2.1 AtlasTrace 진단판에서 **런타임 추적 기능을 완전히 제거한 배포용 버전**입니다.

제거됨:
- `TraceAtlasErrors`
- `MaxAtlasErrorTraces`
- `[ATLAS-TRACE]`
- `TMP_FontAsset.TryAddCharacter(s)` 진단 Harmony 패치
- 4096 atlas 오류 스냅샷/스택 덤프

유지됨:
- Noto Sans KR 기반 사전 생성 TMP Font Asset
- Static atlas
- 2048×2048 multi-atlas
- 약 16 atlas 페이지
- 런타임 glyph 생성 없음
- fallback-only routing
- `TMP_Text.font` 강제 교체 없음
- NFC 정규화
- `TMP_InputField`/한글 IME 대응
- CHZZK 문자열과 게임 직접 입력을 동일한 렌더링 경로에서 처리

## 빌드 환경

테스트 기준:
- Cult of the Lamb
- BepInEx 5.4.21
- Unity runtime 2022.3.62
- Unity Editor 2022.3.62f3
- .NET Framework target: net472

## 1. TMP Essential Resources 준비

`FontBundleBuilder` 프로젝트를 Unity 2022.3.62f3에서 한 번 열고:

`Window -> TextMeshPro -> Import TMP Essential Resources`

를 실행한 뒤 Unity를 종료합니다.

이미 Import되어 있다면 다시 할 필요 없습니다.

## 2. 폰트 번들 생성

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\build-font-bundle.ps1 `
  -FontFile "C:\Users\sjnote\Downloads\Noto_Sans_KR\static\NotoSansKR-Regular.ttf" `
  -UnityEditor "D:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
```

기존에 정상 생성된 v4.1/v4.2용 `koreanfont.bundle`이 있다면 재사용할 수 있습니다.
프로젝트 최상위의 `build-plugin.ps1` 옆에 두면 됩니다.

## 3. 플러그인 빌드 및 로컬 설치

```powershell
.\build-plugin.ps1
```

정상 설치 위치:

```text
Cult of the Lamb
└─ BepInEx
   └─ plugins
      └─ COTL_KoreanFontFix
         ├─ COTL_KoreanFontFix.dll
         └─ koreanfont.bundle
```

## 4. 스트리머 배포용 ZIP 만들기

플러그인 빌드/설치가 끝난 뒤:

```powershell
.\package-release.ps1
```

실행하면:

```text
Release\COTL_KoreanFontFix_v4.3.0.zip
```

이 생성됩니다.

이 ZIP은 스트리머에게 그대로 전달할 수 있습니다.

## Config

게임 첫 실행 후:

```text
BepInEx\config\kr.ruon.cotl.koreanfontfix.cfg
```

기본 설정:

```ini
[Debug]
VerboseLog = false

[Text]
NormalizeHangulToNFC = true
```

진단판의 `TraceAtlasErrors`, `MaxAtlasErrorTraces` 설정은 4.3.0에서 더 이상 사용하지 않습니다.
기존 cfg에 해당 항목이 남아 있어도 플러그인이 읽지 않습니다.

## 정상 로드 로그

```text
Loading [COTL Korean Font Fix 4.3.0]
Loaded bundled Korean TMP font: KoreanRuntimeFont, dynamic=Static, multiAtlas=True, atlas=2048x2048
TMP_Text and TMP_InputField Harmony patches installed.
COTL Korean Font Fix 4.3.0 loaded. RuntimeFont=KoreanRuntimeFont, routing=fallback-only
```

## 주의

게임 자체가 출력하는 다음 로그는 이 플러그인에서 숨기거나 수정하지 않습니다.

```text
Required atlas size exceeds supported max (4096x4096)
```

4.2.1 진단 결과, KoreanRuntimeFont의 런타임 atlas 확장과 직접 연결되는 증거는 확인되지 않았기 때문에 Release 버전에서는 해당 오류를 건드리지 않습니다.
