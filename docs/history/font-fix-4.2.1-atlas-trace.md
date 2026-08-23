# font-fix-4.2.1-atlas-trace 개발 요약

## 목표

Add a trace-enabled Korean font patch and a Unity bundle builder to diagnose atlas replacement behavior.

## 실제 변경 파일

- `A	tools/COTL_KoreanFontFix/FontBundleBuilder/Assets/Editor/KoreanFontBundleBuilder.cs`
- `A	tools/COTL_KoreanFontFix/FontBundleBuilder/Assets/Input/.keep`
- `A	tools/COTL_KoreanFontFix/FontBundleBuilder/Packages/manifest.json`
- `A	tools/COTL_KoreanFontFix/GamePlugin/COTL_KoreanFontFix.csproj`
- `A	tools/COTL_KoreanFontFix/GamePlugin/Plugin.cs`
- `A	tools/COTL_KoreanFontFix/README.md`
- `A	tools/COTL_KoreanFontFix/build-font-bundle.ps1`
- `A	tools/COTL_KoreanFontFix/build-plugin.ps1`

## 복원 근거

- 보존 소스: `COTL_KoreanFontFix_v4.2.1_AtlasTrace.zip`
- 보존 시각 기준: `2026-08-23T02:14:36+00:00`
- 이전 스냅샷과의 실제 파일 차이를 사용해 복원함

## 커밋

`feat(font): add atlas tracing diagnostics`

> 이 문서는 2026년 8월에 보존된 소스 스냅샷과 당시 프로젝트 문서를 기준으로 사후 복원되었습니다.
