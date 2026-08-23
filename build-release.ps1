$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist\ChzzkOfTheLamb-v1.0.0'
$companionOut = Join-Path $dist 'Companion'
$pluginOut = Join-Path $dist 'Plugin'
$fontOut = Join-Path $dist 'BundledMods\COTL_KoreanFontFix'
$fontAssetRoot = Join-Path $root 'release-assets\COTL_KoreanFontFix'
$fontDll = Join-Path $fontAssetRoot 'COTL_KoreanFontFix.dll'
$fontBundle = Join-Path $fontAssetRoot 'koreanfont.bundle'

Write-Host '[1/7] Validating bundled release assets...'
if (-not (Test-Path $fontDll)) {
    throw "Missing font patch binary: $fontDll`nPlace the tested COTL Korean Font Fix 4.2.1 DLL in release-assets\COTL_KoreanFontFix before building the external release."
}
if (-not (Test-Path $fontBundle)) {
    throw "Missing font bundle: $fontBundle`nPlace the tested koreanfont.bundle used by COTL Korean Font Fix 4.2.1 in release-assets\COTL_KoreanFontFix before building the external release."
}

Write-Host '[2/7] Cleaning dist...'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $companionOut, $pluginOut, $fontOut | Out-Null

Write-Host '[3/7] Restore...'
dotnet restore (Join-Path $root 'ChzzkOfTheLamb.sln')

Write-Host '[4/7] Building game mod...'
dotnet build (Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj') -c Release --no-restore
$modBin = Join-Path $root 'src\ChzzkOfTheLamb.Mod\bin\Release'
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Mod.dll') $pluginOut -Force
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Protocol.dll') $pluginOut -Force

Write-Host '[5/7] Publishing self-contained Companion...'
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $companionOut

Write-Host '[6/7] Bundling Korean font patch 4.2.1...'
Copy-Item $fontDll $fontOut -Force
Copy-Item $fontBundle $fontOut -Force
Copy-Item (Join-Path $root 'Install-ChzzkOfTheLamb.ps1') $dist -Force
Copy-Item (Join-Path $root 'RELEASE-README.md') $dist -Force

$manifest = @"
ChzzkOfTheLamb v1.0.0
Bundled components:
- ChzzkOfTheLamb Mod
- ChzzkOfTheLamb Companion (win-x64 self-contained)
- COTL Korean Font Fix 4.2.1
  - COTL_KoreanFontFix.dll
  - koreanfont.bundle

Font patch install path:
BepInEx\plugins\COTL_KoreanFontFix
"@
Set-Content -Path (Join-Path $dist 'BUNDLED-COMPONENTS.txt') -Value $manifest -Encoding UTF8

Write-Host '[7/7] Creating ZIP...'
$zip = Join-Path (Split-Path $dist -Parent) 'ChzzkOfTheLamb-v1.0.0-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Release created: $zip"
