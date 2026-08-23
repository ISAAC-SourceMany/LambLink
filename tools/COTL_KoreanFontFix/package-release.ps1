param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = "4.3.0"

function Find-CotlGameDir {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Cult of the Lamb",
        "C:\Program Files\Steam\steamapps\common\Cult of the Lamb"
    )

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $path "BepInEx\plugins\COTL_KoreanFontFix\COTL_KoreanFontFix.dll")) {
            return $path
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = Find-CotlGameDir
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "Installed Cult of the Lamb plugin was not found. Run .\build-plugin.ps1 first or pass -GameDir."
}

$installedDir =
    Join-Path `
        $GameDir `
        "BepInEx\plugins\COTL_KoreanFontFix"

$dll =
    Join-Path `
        $installedDir `
        "COTL_KoreanFontFix.dll"

$bundle =
    Join-Path `
        $installedDir `
        "koreanfont.bundle"

if (-not (Test-Path -LiteralPath $dll)) {
    throw "Installed DLL missing: $dll"
}

if (-not (Test-Path -LiteralPath $bundle)) {
    throw "Installed bundle missing: $bundle"
}

$releaseRoot =
    Join-Path `
        $scriptRoot `
        "Release"

$packageName =
    "COTL_KoreanFontFix_v$version"

$packageDir =
    Join-Path `
        $releaseRoot `
        $packageName

$pluginDir =
    Join-Path `
        $packageDir `
        "BepInEx\plugins\COTL_KoreanFontFix"

$zip =
    Join-Path `
        $releaseRoot `
        ($packageName + ".zip")

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item `
        -LiteralPath $packageDir `
        -Recurse `
        -Force
}

if (Test-Path -LiteralPath $zip) {
    Remove-Item `
        -LiteralPath $zip `
        -Force
}

New-Item `
    -ItemType Directory `
    -Path $pluginDir `
    -Force |
    Out-Null

Copy-Item `
    -LiteralPath $dll `
    -Destination (Join-Path $pluginDir "COTL_KoreanFontFix.dll") `
    -Force

Copy-Item `
    -LiteralPath $bundle `
    -Destination (Join-Path $pluginDir "koreanfont.bundle") `
    -Force

$installText = @"
COTL Korean Font Fix $version

설치 방법
1. Cult of the Lamb와 BepInEx를 종료합니다.
2. 이 압축파일 안의 BepInEx 폴더를 게임 설치 폴더에 그대로 덮어씁니다.
3. 최종 경로가 아래와 같은지 확인합니다.

BepInEx\plugins\COTL_KoreanFontFix\COTL_KoreanFontFix.dll
BepInEx\plugins\COTL_KoreanFontFix\koreanfont.bundle

필수 환경
- BepInEx 5.4.x
- Cult of the Lamb / Unity 2022.3 계열
- COTL_API 및 CHZZK 연동 프로그램과 함께 사용할 수 있음

이 플러그인은 CHZZK 전용이 아닙니다.
게임 내 직접 한글 입력과 외부 연동 문자열 모두 동일한 TextMeshPro fallback 경로로 처리합니다.
"@

Set-Content `
    -LiteralPath (Join-Path $packageDir "INSTALL_KO.txt") `
    -Value $installText `
    -Encoding UTF8

Compress-Archive `
    -Path (Join-Path $packageDir "*") `
    -DestinationPath $zip `
    -CompressionLevel Optimal

Write-Host ""
Write-Host "Streamer-ready release package created." -ForegroundColor Green
Write-Host "ZIP: $zip"
