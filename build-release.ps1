$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = '1.0.0-rc31'
$distRoot = Join-Path $root 'dist'
$dist = Join-Path $distRoot "ChzzkOfTheLamb-v$release"
$companionOut = Join-Path $dist 'Companion'
$pluginOut = Join-Path $dist 'Plugin'
$fontAssetRoot = Join-Path $root 'release-assets\COTL_KoreanFontFix'
$fontDll = Join-Path $fontAssetRoot 'COTL_KoreanFontFix.dll'
$fontBundle = Join-Path $fontAssetRoot 'koreanfont.bundle'
$hosting = Join-Path $root 'release-hosting'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Write-Host '[0/9] Verifying RC31 source identity and removing stale compiler outputs...'
$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj' = '5e2aac6c30559e9bc2fd2fddb131aeb1f95e60d46faec187b71ec612dd63a938'
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'be158337771ce3cc70c1b1f6439c340f41efc9c72e52388c005b9abc2d38a223'
  'src\ChzzkOfTheLamb.Mod\BridgeRuntimeHost.cs' = '9261721e2708f59debb6785e2eeec611063738bd36744a55dec94f529ae3bc6b'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = 'dbe786dbd6cfa0df9144c87820e696b5ec076a8ee9c3f5b018b358179ff15595'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '753830ca22dc89e571da9861fac0ab2a0306497de81c89482d6f7fbccfa026eb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerNameplatePatch.cs' = '51a4385cb3cb0a801b89ed7923e95133a920e80a9ed57446b8437abe0e79abdf'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '39e2b9939254ffb233b2ea075c627683789241e0a037ec7c02c74d1ccb94746c'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerAppearanceService.cs' = 'e840ef802b18b8c52155c01f63bf0e1d3bc8d69e2f433407f7c2d7eb3e08ddc4'
  'src\ChzzkOfTheLamb.Mod\Game\GameSaveService.cs' = '5b48b8ce1f0c50ae47a9e160ce4244ab9b3712c2cc96e8cc55678163ded72c5d'
  'src\ChzzkOfTheLamb.Mod\Game\DonationEffectService.cs' = 'f5c52600b34d34b9439a2ad69919f520830a81e7f69f578b0c3fa6a9d8126e3f'
  'src\ChzzkOfTheLamb.Mod\Game\DonationGameplayGate.cs' = '1120652d09990ac571106e105e8dc7f91a0d3f308f321405674a2132096e77bf'
  'src\ChzzkOfTheLamb.Mod\Game\DonationStoryLifecycle.cs' = 'ee98c5488b3e3bbdad64f9b2049af27f18003d0ce666ac8dbb189eea9808fa05'
  'src\ChzzkOfTheLamb.Mod\Game\DungeonDonationBuffs.cs' = '2c03077f1f54db5df5bc085665243cb7f240d4f606abeeb1bff49876f7d26120'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '55312e81b19340d18188ad0cf6efbcb7a06f6bef0ccb243f1ae568172d401a64'
  'src\ChzzkOfTheLamb.Companion\Program.cs' = '43532c7a6a1c6742d6fd610152c3fd626c3cfb1064be028ad1e7d0b877930f79'
  'src\ChzzkOfTheLamb.Companion\Chzzk\ChzzkRealtimeClient.cs' = '09ce1e58e9308f7fecfb320635b3e5b32f6c2c0a9414d0200594226df18a7c8a'
  'src\ChzzkOfTheLamb.Companion\ViewerPage\ViewerPageShare.cs' = '2e9041cf4209e76f258c7a0cdab0847431f4affef002bd03b1ee37efef36692a'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = '6199cf43fea8adbcb9b166f31370975f2ac954af3834bbdf3866142e55fe8da9'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\TeeTextWriter.cs' = '3edd24b12f8aaac9a1de768be84d0d6711c1b30c28a8bff39507912ffffcd305'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\RollingFileTextWriter.cs' = 'c44b021eda8285598fbfff015881f3406fef8c84545d08f78db757a70b81793a'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\DonationTraceRegistry.cs' = 'c56b6c0f3970917911b1ef21eae00c543b4783eaeb1d1f445ec2ccf9da9061e8'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\DiagnosticPrivacy.cs' = 'c9afc180f204d35c8175e1be3c37167686add4e8c43b8d737e0c7a00ce406f36'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\SupportBundleService.cs' = 'd17c653e9698dd32157ac88d28bf243aa8df45ff616dbb1df201412b33248ba3'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\Overlay\RaffleOverlayServer.cs' = '259e0103fbb0b4e65fd996e3af01c57a265567d6a643de7f9c9b833a11522b0d'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = '10a4171a57bc1d5efda0a0de1fd5f97c4e4f4a9d4090b9e6763d13565c105536'
  'src\ChzzkOfTheLamb.Installer\Program.cs' = '9fe45e58353de2d669727a6f1205d2085417a2e07d418010c5c8f746aec92e20'
  'src\ChzzkOfTheLamb.Installer\ChzzkOfTheLamb.Installer.csproj' = '74b9fb37e618da56a14a81d815c47e698ca981b2ba22cdd31a9568a3657dcc64'
  'installer\installer-manifest.template.json' = '3a8f595c4985352d8315350797403f52eee7558f32ec3e843e0d8a3294284646'
  'prepare-installer-manifest.ps1' = 'dd04ed6e9d0bd5e42714012bebb69ccf8bf050dd28124f48534a78da90bf2026'
  'build-distribution.ps1' = 'fba38294213295b3123fa10a63546c1eaa0a31f5abf4858e7934ea869ec12969'
  'DISTRIBUTION-RC31.md' = '11a9ae3e6155c897e4fb2f1b685ddbc76d5739e7d63cc56e74527e0bd6d189b1'
}
foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC31 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC31 source does not match the reviewed version: $relativePath"
  }
}
Write-Host '[VERIFY] Critical RC31 source hashes OK.'

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
foreach ($f in @('COTL-KoreanFontFix-4.2.1-rc31.zip',"ChzzkOfTheLamb-Mod-$release.zip","ChzzkOfTheLamb-Companion-$release-win-x64.zip","ChzzkOfTheLamb-Setup-$release.exe","installer-manifest-$release.json")) {
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
$buildTag = 'rc31-donation-safe-runtime-paused-buffs'
$hasBuildTag = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($buildTag))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($buildTag)))
if (-not $hasBuildTag) {
  throw 'Built mod DLL does not contain the RC31 build tag. Refusing to package a stale DLL.'
}
foreach ($marker in @('io.github.xhayper.COTL_API', 'RAFFLE_ROUND_CLOSED', '[NAMEPLATE][PATCH-VERIFY]', '[NAMEPLATE][INLINE-APPLIED]', '[IDENTITY-COMMIT]', 'CHZZK nameplate marker dropped', '<color=#00C471>Chzzk</color> ')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing required RC31 marker: $marker" }
}
foreach ($forbidden in @('[NAMEPLATE][IDENTITY-REPAIRED]', '[FOLLOWER-MARKER][IDENTITY-REPAIRED]')) {
  $hasForbidden = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
                  (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($hasForbidden) { throw "Built Mod contains forbidden ID-only identity repair marker: $forbidden" }
}
foreach ($marker in @('[DONATION][RX]', '[DONATION][APPLIED]', '[DONATION][RESULT-TX]', '[DONATION][QUEUE][ENQUEUED]', '[DONATION][GATE][STATE]', '[DONATION][STORY-HOOK][CAPABILITY]', 'DONATION_RUNTIME_STATE', 'stage=')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing RC31 donation diagnostic marker: $marker" }
}
Write-Host "[VERIFY] RC31 mod build tag and donation diagnostics found; SHA-256=$((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"

Write-Host '[5/9] Building and validating Companion diagnostics...'
$companionProject = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
dotnet build $companionProject -c Release --no-restore
Assert-NativeSuccess 'Release Companion validation build'
$companionValidationDll = Join-Path $root 'src\ChzzkOfTheLamb.Companion\bin\Release\net8.0\ChzzkOfTheLamb.Companion.dll'
if (-not (Test-Path $companionValidationDll)) { throw "Companion validation assembly was not produced: $companionValidationDll" }
$companionValidationBytes = [System.IO.File]::ReadAllBytes($companionValidationDll)
foreach ($forbidden in @('[FOLLOWER-MIGRATION][RC26-RESTORED]', 'name drift retained for repair')) {
  $hasForbidden = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
                  (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($hasForbidden) { throw "Built Companion contains forbidden unsaved-result recovery marker: $forbidden" }
}
foreach ($marker in @('[DONATION][TERMINAL][ACK-TIMEOUT]', '[DONATION][GATE][RX]', 'pausedWhileModGateBlocked=true', '[OVERLAY][BUFF-TIMER][PAUSED]', 'DONATION_GATE=', '[SUPPORT][READY]', 'companion-rc31.log')) {
  $hasMarker = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built Companion validation assembly is missing RC31 diagnostic marker: $marker" }
}
foreach ($forbiddenMarker in @('RC31_TEST_TOOLS enabled')) {
  $hasForbidden = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($forbiddenMarker))) -or
                  (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($forbiddenMarker)))
  if ($hasForbidden) { throw "Release Companion unexpectedly contains test tools: $forbiddenMarker" }
}
Write-Host '[VERIFY] RC31 support and donation markers found in compiled Companion assembly.'

Write-Host '[5/9] Publishing Companion self-contained single-file...'
dotnet publish $companionProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $companionOut
Assert-NativeSuccess 'Release Companion publish'
$companionExe = Join-Path $companionOut 'ChzzkOfTheLamb.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($companionVersion.FileVersion -ne '1.0.0.31') { throw "Unexpected Companion file version: $($companionVersion.FileVersion)" }
Write-Host '[VERIFY] RC31 Companion EXE exists and file version is 1.0.0.31.'

Write-Host '[6/9] Creating normalized downloadable component ZIPs...'
$temp = Join-Path $distRoot '_component-build'
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$modPkg = Join-Path $temp 'mod\BepInEx\plugins\ChzzkOfTheLamb'
New-Item -ItemType Directory -Force -Path $modPkg | Out-Null
Copy-Item (Join-Path $pluginOut '*') $modPkg -Force
Compress-Archive -Path (Join-Path $temp 'mod\*') -DestinationPath (Join-Path $hosting "ChzzkOfTheLamb-Mod-$release.zip") -CompressionLevel Optimal

$fontPkg = Join-Path $temp 'font\BepInEx\plugins\COTL_KoreanFontFix'
New-Item -ItemType Directory -Force -Path $fontPkg | Out-Null
Copy-Item $fontDll (Join-Path $fontPkg 'COTL_KoreanFontFix.dll') -Force
Copy-Item $fontBundle (Join-Path $fontPkg 'koreanfont.bundle') -Force
Compress-Archive -Path (Join-Path $temp 'font\*') -DestinationPath (Join-Path $hosting 'COTL-KoreanFontFix-4.2.1-rc31.zip') -CompressionLevel Optimal

Compress-Archive -Path (Join-Path $companionOut '*') -DestinationPath (Join-Path $hosting "ChzzkOfTheLamb-Companion-$release-win-x64.zip") -CompressionLevel Optimal

Write-Host '[7/9] Publishing single-file GUI installer...'
$installerPublish = Join-Path $distRoot '_installer-publish'
if (Test-Path $installerPublish) { Remove-Item $installerPublish -Recurse -Force }
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Installer\ChzzkOfTheLamb.Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $installerPublish
Assert-NativeSuccess 'Release Installer publish'
$installerExe = Join-Path $installerPublish 'ChzzkOfTheLamb.Installer.exe'
if (-not (Test-Path $installerExe)) { throw "Installer EXE was not produced: $installerExe" }
$installerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installerExe)
if ($installerVersion.FileVersion -ne '1.0.0.31') { throw "Unexpected Installer file version: $($installerVersion.FileVersion)" }
if (-not $installerVersion.ProductVersion.StartsWith($release, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Unexpected Installer product version: $($installerVersion.ProductVersion)"
}
Copy-Item $installerExe (Join-Path $hosting "ChzzkOfTheLamb-Setup-$release.exe") -Force

Write-Host '[8/9] Creating legacy test ZIP + release docs...'
Copy-Item (Join-Path $root 'RELEASE-README.md') $dist -Force
$zip = Join-Path $distRoot "ChzzkOfTheLamb-v$release-win-x64-legacy.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '[9/9] Finished local build.'
Write-Host "Installer EXE: $(Join-Path $hosting "ChzzkOfTheLamb-Setup-$release.exe")"
Write-Host ".\prepare-installer-manifest.ps1 generates installer-manifest-$release.json."
Write-Host "Recommended: run .\build-distribution.ps1 to validate and assemble the complete $release handoff bundle."
