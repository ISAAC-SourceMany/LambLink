# COTL Korean Font Fix v4.2.1 — Atlas Trace

이 프로젝트는 이전 대화에서 검증한 수정사항을 누적 반영한 버전입니다.

- Noto Sans KR 기반 static prebake bundle
- 2048x2048 multi-atlas
- runtime TryAddCharacters 제거
- TMP_Text primary font 강제 교체 제거
- fallback-only routing
- NFC 정규화
- TMP_InputField IME 표시 대응
- build-font-bundle.ps1의 Unity 로그 flush / 성공 마커 타이밍 보강
- 4096x4096 atlas 오류 런타임 추적 추가

## 기존 koreanfont.bundle 재사용

이미 약 20 MB 크기의 v4.1/v4.2 static `koreanfont.bundle`이 있다면 다시 빌드할 필요 없습니다.
그 파일을 이 폴더의 `build-plugin.ps1` 옆에 복사한 뒤:

```powershell
.\build-plugin.ps1
```

## bundle 재빌드

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\build-font-bundle.ps1 `
  -FontFile "C:\Users\sjnote\Downloads\Noto_Sans_KR\static\NotoSansKR-Regular.ttf" `
  -UnityEditor "D:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
```

TMP shader 오류가 나면 FontBundleBuilder를 Unity에서 한 번 열고:
`Window -> TextMeshPro -> Import TMP Essential Resources`
를 실행한 후 재빌드하세요.

## Atlas 추적

기본값으로 켜져 있습니다.

`BepInEx\config\kr.ruon.cotl.koreanfontfix.cfg`

```ini
[Debug]
TraceAtlasErrors = true
MaxAtlasErrorTraces = 12
VerboseLog = false

[Text]
NormalizeHangulToNFC = true
```

게임에서 4096 atlas 오류를 재현한 뒤 `BepInEx\LogOutput.log`에서 다음을 검색하세요.

```text
[ATLAS-TRACE
```

중요한 줄:

```text
[ATLAS-TRACE] Active TMP_FontAsset: ...
[ATLAS-TRACE] Active operation: ...
[ATLAS-TRACE] Arguments: ...
[ATLAS-TRACE] Caller stack captured at font operation:
```
