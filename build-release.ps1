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

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Write-Host '[0/9] Verifying RC26 source identity and removing stale compiler outputs...'
$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj' = '5e2aac6c30559e9bc2fd2fddb131aeb1f95e60d46faec187b71ec612dd63a938'
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'f2669bef8769b8b02444a3503663bfedc3e9ee2b5366fc2971809ebed0819e94'
  'src\ChzzkOfTheLamb.Mod\BridgeRuntimeHost.cs' = '9261721e2708f59debb6785e2eeec611063738bd36744a55dec94f529ae3bc6b'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = 'dbe786dbd6cfa0df9144c87820e696b5ec076a8ee9c3f5b018b358179ff15595'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '753830ca22dc89e571da9861fac0ab2a0306497de81c89482d6f7fbccfa026eb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerNameplatePatch.cs' = 'b2e3d3e7de76e55fcc4d8a1fbb960084db3c2e29d174e093777ef969a2151acb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = 'a752a79f683dac5808b680776f4c9df29765e71fab7c1e859fdea69fd264a99d'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerAppearanceService.cs' = 'e840ef802b18b8c52155c01f63bf0e1d3bc8d69e2f433407f7c2d7eb3e08ddc4'
  'src\ChzzkOfTheLamb.Mod\Game\GameSaveService.cs' = '5b48b8ce1f0c50ae47a9e160ce4244ab9b3712c2cc96e8cc55678163ded72c5d'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '112e647244272e6e950104dad456fb74b8f75d423b9814244ded14e5ef6e116f'
  'src\ChzzkOfTheLamb.Companion\Program.cs' = 'ef40a7cdc427786b8d1da29aa9e0bb4dcb95aadbf86dc31dae8d63f638b9f9f5'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = '6199cf43fea8adbcb9b166f31370975f2ac954af3834bbdf3866142e55fe8da9'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\TeeTextWriter.cs' = '3edd24b12f8aaac9a1de768be84d0d6711c1b30c28a8bff39507912ffffcd305'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\Overlay\RaffleOverlayServer.cs' = '9a96dbc2b08020fc4d76c51174fa8bc3add4fdae978bf227c1c968b49d726325'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = '4cbe8b429fd6da409f91c61d38685b43a38d9c674cb9b5a520552558a13f6e67'
}
foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC26 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC26 source does not match the reviewed version: $relativePath"
  }
}
Write-Host '[VERIFY] Critical RC26 source hashes OK.'

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
Assert-NativeSuccess 'Release restore'

Write-Host '[4/9] Building game mod...'
dotnet build (Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj') -c Release --no-restore
Assert-NativeSuccess 'Release Mod build'
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
$buildTag = 'rc26-nameplate-layout-lifecycle-fix'
$hasBuildTag = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($buildTag))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($buildTag)))
if (-not $hasBuildTag) {
  throw 'Built mod DLL does not contain the RC26 build tag. Refusing to package a stale DLL.'
}
foreach ($marker in @('io.github.xhayper.COTL_API', 'RAFFLE_ROUND_CLOSED', '[NAMEPLATE][PATCH-VERIFY]', 'estimated-stale-layout-fallback')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing required RC26 marker: $marker" }
}
Write-Host "[VERIFY] RC26 mod build tag found; SHA-256=$((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"

Write-Host '[5/9] Publishing Companion self-contained single-file...'
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $companionOut
Assert-NativeSuccess 'Release Companion publish'
$companionExe = Join-Path $companionOut 'ChzzkOfTheLamb.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($companionVersion.FileVersion -ne '1.0.0.26') { throw "Unexpected Companion file version: $($companionVersion.FileVersion)" }
Write-Host '[VERIFY] RC26 Companion version tag found.'

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
Assert-NativeSuccess 'Release Installer publish'
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
