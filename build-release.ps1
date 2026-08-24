$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distRoot = Join-Path $root 'dist'
$dist = Join-Path $distRoot 'ChzzkOfTheLamb-v1.0.0'
$companionOut = Join-Path $dist 'Companion'
$pluginOut = Join-Path $dist 'Plugin'
$fontAssetRoot = Join-Path $root 'release-assets\COTL_KoreanFontFix'
$fontDll = Join-Path $fontAssetRoot 'COTL_KoreanFontFix.dll'
$fontBundle = Join-Path $fontAssetRoot 'koreanfont.bundle'
$hosting = Join-Path $root 'release-hosting'

Write-Host '[0/9] Verifying RC21 source identity and removing stale compiler outputs...'
$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = '443e3d908c06841dc62731a5fd441b309b7b1161c066cb1389bb7c9d4aba56de'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = 'f4880b66af179cdb341abee543fd1d4e0c40a23d893cdd57ad7571b61cac7bbf'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '81260035a8f74e613b414576d1065373c76fcdccca000a790097fa4047b2f9cb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '6883d37210328c161ead327c6d4c9ff1576c96aa8793790872d4d8791f7a0ee9'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerAppearanceService.cs' = 'e840ef802b18b8c52155c01f63bf0e1d3bc8d69e2f433407f7c2d7eb3e08ddc4'
  'src\ChzzkOfTheLamb.Mod\Game\GameSaveService.cs' = '5b48b8ce1f0c50ae47a9e160ce4244ab9b3712c2cc96e8cc55678163ded72c5d'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = 'd64b622d34e9956dbdf953a1e36c8a0cf4b94be1ad0635a07f46c7ff0e180c1e'
  'src\ChzzkOfTheLamb.Companion\Program.cs' = '0400585c337f006b6bc5e8e89e24383d36677ad23d6bde2160656ca5e5b81543'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = '7f5070e83242227d5baee5c64c6565f6f0b8020d54ff280a2f96daea5eb47fdd'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\TeeTextWriter.cs' = '3edd24b12f8aaac9a1de768be84d0d6711c1b30c28a8bff39507912ffffcd305'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = 'ee53443a76a2fe34ff22a09f8f086a6fb057eb2e2c7942f7b2780f0eabfdd79d'
}
foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC21 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC21 source does not match the reviewed version: $relativePath"
  }
}
Write-Host '[VERIFY] Critical RC21 source hashes OK.'

Get-ChildItem -Path (Join-Path $root 'src') -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin', 'obj') } |
  Sort-Object FullName -Descending |
  Remove-Item -Recurse -Force

Write-Host '[1/9] Validating Korean font patch assets...'
if (-not (Test-Path $fontDll)) { throw "Missing: $fontDll" }
if (-not (Test-Path $fontBundle)) { throw "Missing: $fontBundle" }
$expectedFontDllSha256 = 'f51607271da49cbce4085ae30423aa133b7dc20bb374c16d919a95a0833036d8'
$expectedFontBundleSha256 = 'd711731544f49c59423715885e4466b75a135767515ab3d3ae4de58df4123a8f'
if ((Get-FileHash $fontDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedFontDllSha256) { throw 'Unexpected COTL_KoreanFontFix.dll' }
if ((Get-FileHash $fontBundle -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedFontBundleSha256) { throw 'Unexpected koreanfont.bundle' }
Write-Host '[ASSET] Korean Font Fix 4.2.1 verified.'

Write-Host '[2/9] Cleaning dist/release-hosting component artifacts...'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $companionOut, $pluginOut, $hosting | Out-Null
foreach ($f in @('COTL-KoreanFontFix-4.2.1.zip','ChzzkOfTheLamb-Mod-1.0.0.zip','ChzzkOfTheLamb-Companion-1.0.0-win-x64.zip','ChzzkOfTheLamb-Setup-1.0.0.exe','installer-manifest.json')) {
  $p = Join-Path $hosting $f; if (Test-Path $p) { Remove-Item $p -Force }
}

Write-Host '[3/9] Restore...'
dotnet restore (Join-Path $root 'ChzzkOfTheLamb.sln')

Write-Host '[4/9] Building game mod...'
dotnet build (Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj') -c Release --no-restore
$modBin = Join-Path $root 'src\ChzzkOfTheLamb.Mod\bin\Release'
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Mod.dll') $pluginOut -Force
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Protocol.dll') $pluginOut -Force

$modDll = Join-Path $pluginOut 'ChzzkOfTheLamb.Mod.dll'
$modBytes = [System.IO.File]::ReadAllBytes($modDll)
function Test-ByteSequence([byte[]]$Haystack, [byte[]]$Needle) {
  if ($Needle.Length -eq 0 -or $Haystack.Length -lt $Needle.Length) { return $false }
  for ($i = 0; $i -le $Haystack.Length - $Needle.Length; $i++) {
    $matched = $true
    for ($j = 0; $j -lt $Needle.Length; $j++) {
      if ($Haystack[$i + $j] -ne $Needle[$j]) { $matched = $false; break }
    }
    if ($matched) { return $true }
  }
  return $false
}
$buildTag = 'rc21-diagnostic-watchdog-fallback'
$hasBuildTag = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($buildTag))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($buildTag)))
if (-not $hasBuildTag) {
  throw 'Built mod DLL does not contain the RC21 build tag. Refusing to package a stale DLL.'
}
Write-Host "[VERIFY] RC21 mod build tag found; SHA-256=$((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"

Write-Host '[5/9] Publishing Companion self-contained single-file...'
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $companionOut
$companionExe = Join-Path $companionOut 'ChzzkOfTheLamb.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($companionVersion.FileVersion -ne '1.0.0.21') { throw "Unexpected Companion file version: $($companionVersion.FileVersion)" }
Write-Host '[VERIFY] RC21 Companion version tag found.'

Write-Host '[6/9] Creating normalized downloadable component ZIPs...'
$temp = Join-Path $distRoot '_component-build'
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$modPkg = Join-Path $temp 'mod\BepInEx\plugins\ChzzkOfTheLamb'
New-Item -ItemType Directory -Force -Path $modPkg | Out-Null
Copy-Item (Join-Path $pluginOut '*') $modPkg -Force
Compress-Archive -Path (Join-Path $temp 'mod\*') -DestinationPath (Join-Path $hosting 'ChzzkOfTheLamb-Mod-1.0.0.zip') -CompressionLevel Optimal

$fontPkg = Join-Path $temp 'font\BepInEx\plugins\COTL_KoreanFontFix'
New-Item -ItemType Directory -Force -Path $fontPkg | Out-Null
Copy-Item $fontDll (Join-Path $fontPkg 'COTL_KoreanFontFix.dll') -Force
Copy-Item $fontBundle (Join-Path $fontPkg 'koreanfont.bundle') -Force
Compress-Archive -Path (Join-Path $temp 'font\*') -DestinationPath (Join-Path $hosting 'COTL-KoreanFontFix-4.2.1.zip') -CompressionLevel Optimal

Compress-Archive -Path (Join-Path $companionOut '*') -DestinationPath (Join-Path $hosting 'ChzzkOfTheLamb-Companion-1.0.0-win-x64.zip') -CompressionLevel Optimal

Write-Host '[7/9] Publishing single-file GUI installer...'
$installerPublish = Join-Path $distRoot '_installer-publish'
if (Test-Path $installerPublish) { Remove-Item $installerPublish -Recurse -Force }
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Installer\ChzzkOfTheLamb.Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $installerPublish
$installerExe = Join-Path $installerPublish 'ChzzkOfTheLamb.Installer.exe'
if (-not (Test-Path $installerExe)) { throw "Installer EXE was not produced: $installerExe" }
Copy-Item $installerExe (Join-Path $hosting 'ChzzkOfTheLamb-Setup-1.0.0.exe') -Force

Write-Host '[8/9] Creating legacy test ZIP + release docs...'
Copy-Item (Join-Path $root 'RELEASE-README.md') $dist -Force
$zip = Join-Path $distRoot 'ChzzkOfTheLamb-v1.0.0-win-x64-legacy.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '[9/9] Finished local build.'
Write-Host "Installer EXE: $(Join-Path $hosting 'ChzzkOfTheLamb-Setup-1.0.0.exe')"
Write-Host 'Now run .\prepare-installer-manifest.ps1 to fetch/hash official BepInEx + COTL_API and generate installer-manifest.json.'
Write-Host 'Then upload release-hosting\ChzzkOfTheLamb-Setup-1.0.0.exe for users, plus your own component ZIPs and installer-manifest.json to CloudFront /releases/.'
