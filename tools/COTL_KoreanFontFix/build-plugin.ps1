param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot "GamePlugin\COTL_KoreanFontFix.csproj"
$bundle = Join-Path $scriptRoot "koreanfont.bundle"

function Find-CotlGameDir {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Cult of the Lamb",
        "C:\Program Files\Steam\steamapps\common\Cult of the Lamb"
    )

    foreach ($path in $candidates) {
        $tmpDll =
            Join-Path `
                $path `
                "Cult Of The Lamb_Data\Managed\Unity.TextMeshPro.dll"

        if (Test-Path -LiteralPath $tmpDll) {
            return $path
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = Find-CotlGameDir
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "Cult of the Lamb install was not found. Pass -GameDir explicitly."
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project file missing: $project"
}

if (-not (Test-Path -LiteralPath $bundle)) {
    throw "koreanfont.bundle missing beside build-plugin.ps1. Build or copy it first."
}

$required = @(
    (Join-Path $GameDir "BepInEx\core\BepInEx.dll"),
    (Join-Path $GameDir "BepInEx\core\0Harmony.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.CoreModule.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.InputLegacyModule.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.AssetBundleModule.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.TextRenderingModule.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\UnityEngine.UI.dll"),
    (Join-Path $GameDir "Cult Of The Lamb_Data\Managed\Unity.TextMeshPro.dll")
)

foreach ($requiredFile in $required) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required game/mod assembly missing: $requiredFile"
    }
}

Write-Host "Game directory: $GameDir"

dotnet build `
    $project `
    -c Release `
    -p:GameDir="$GameDir"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$dll =
    Join-Path `
        $scriptRoot `
        "GamePlugin\bin\Release\net472\COTL_KoreanFontFix.dll"

if (-not (Test-Path -LiteralPath $dll)) {
    throw "Built DLL missing: $dll"
}

$installDir =
    Join-Path `
        $GameDir `
        "BepInEx\plugins\COTL_KoreanFontFix"

New-Item `
    -ItemType Directory `
    -Path $installDir `
    -Force |
    Out-Null

Copy-Item `
    -LiteralPath $dll `
    -Destination (Join-Path $installDir "COTL_KoreanFontFix.dll") `
    -Force

Copy-Item `
    -LiteralPath $bundle `
    -Destination (Join-Path $installDir "koreanfont.bundle") `
    -Force

Write-Host ""
Write-Host "COTL Korean Font Fix 4.3.0 installed successfully." -ForegroundColor Green
Write-Host "DLL:    $(Join-Path $installDir 'COTL_KoreanFontFix.dll')"
Write-Host "Bundle: $(Join-Path $installDir 'koreanfont.bundle')"
Write-Host ""
Write-Host "Optional distribution package:"
Write-Host "  .\package-release.ps1"
