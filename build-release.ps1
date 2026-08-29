$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = '1.0.0'
$fileVersion = '1.0.0.40'
$fontPackageName = 'COTL-KoreanFontFix-4.2.1-v1.0.0.zip'
$distRoot = Join-Path $root 'dist'
$dist = Join-Path $distRoot "LambLink-v$release"
$companionOut = Join-Path $dist 'Companion'
$pluginOut = Join-Path $dist 'Plugin'
$fontAssetRoot = Join-Path $root 'release-assets\COTL_KoreanFontFix'
$fontDll = Join-Path $fontAssetRoot 'COTL_KoreanFontFix.dll'
$fontBundle = Join-Path $fontAssetRoot 'koreanfont.bundle'
$hosting = Join-Path $root 'release-hosting'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

function Remove-DirectoryTree([string]$Path) {
  $fullPath = [System.IO.Path]::GetFullPath($Path)
  if (-not [System.IO.Directory]::Exists($fullPath)) { return }

  try {
    Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction Stop
  }
  catch {
    if (-not [System.IO.Directory]::Exists($fullPath)) {
      Write-Host "[CLEAN] Removed after transient PowerShell path race: $fullPath"
      return
    }

    $extendedPath = if ($fullPath.StartsWith('\\')) {
      '\\?\UNC\' + $fullPath.Substring(2)
    } else {
      '\\?\' + $fullPath
    }

    try {
      [System.IO.Directory]::Delete($extendedPath, $true)
    }
    catch {
      if ([System.IO.Directory]::Exists($fullPath)) {
        throw "Failed to clean build directory: $fullPath ($($_.Exception.Message))"
      }
    }
  }

  if ([System.IO.Directory]::Exists($fullPath)) {
    throw "Build directory still exists after cleanup: $fullPath"
  }
}

function Clear-CompilerOutputs {
  $srcRoot = Join-Path $root 'src'
  foreach ($projectDir in @(Get-ChildItem -LiteralPath $srcRoot -Directory -Force)) {
    Remove-DirectoryTree (Join-Path $projectDir.FullName 'bin')
    Remove-DirectoryTree (Join-Path $projectDir.FullName 'obj')
  }
}

Write-Host '[0/9] Verifying v1.0.0 source identity and removing stale compiler outputs...'
& (Join-Path $root 'tests\brand\test-brand-migration.ps1')
$criticalSources = @{
  'src\LambLink.Mod\LambLink.Mod.csproj' = '4e4912f4e62e273a3a4175b3d000ead95874193f29e545ea4c373805bc77ccf6'
  'src\LambLink.Mod\Plugin.cs' = '594f7159efbcb2cef29615329fc49f47960ba4f0f0e24b6916be5a7d06fe4d41'
  'src\LambLink.Mod\BridgeRuntimeHost.cs' = '499b5a9b14ff2fab6bdb84a9304550dd2d19c079ca50e5a6edbdf08894a66279'
  'src\LambLink.Mod\Network\ModBridgeClient.cs' = 'a355fecbbabe76fa69d5bf089f48a28d9bc60da5ad4233d794c68f18c346b63c'
  'src\LambLink.Mod\Game\IndoctrinationRafflePatch.cs' = '9804b3e1d156bab2981f85ff90a52733b83b6f8719ee48309b7f84a88788fee8'
  'src\LambLink.Mod\Game\FollowerNameplatePatch.cs' = 'fe7d7a9503b982316e3f94d0f02caa939d9876fccc9c5b543e8d54a11e341a39'
  'src\LambLink.Mod\Game\FollowerService.cs' = '4b9313e13d5bd398f633018ee9830ae1731cbdd03d8b63dcff7c6f4268fe9b09'
  'src\LambLink.Mod\Game\FollowerAppearanceService.cs' = '7da41f06f7e22cb0beefaab61c0ec346a8cb533246e1ceb94c21c61df1c45856'
  'src\LambLink.Mod\Game\DonationReceiptStore.cs' = '88e10dc061fba7d273f120bb1c5c7addbdf13032b408cbdc92d897e67f169922'
  'src\LambLink.Mod\Game\GameSaveService.cs' = '9114554d1a1d84692e84470708766ee0fb52a1587c04cae1312d1b984a7a6b28'
  'src\LambLink.Mod\Game\DonationEffectService.cs' = 'ea8776182850a7c5598d7e7d059ff85963b50f4d3c10316ce58aa3cbdc43ed60'
  'src\LambLink.Mod\Game\DonationGameplayGate.cs' = '9b1f8cb026b98f4852cab761ab39075d5cce8167debb35a2e9deb69d1a2c17ca'
  'src\LambLink.Mod\Game\DonationStoryLifecycle.cs' = '4304de3eb5e27937992929253549798e5dd0d26ec3d72629af00808dda108da6'
  'src\LambLink.Mod\Game\DungeonDonationBuffs.cs' = '0945d15961880bcaa9c3d0dfec0cae2302a278f54b49ce41a0db2372b119f905'
  'src\LambLink.Protocol\GameMessages.cs' = '826b2d7429942cfdaaa910f415a54fa0083bb755a3fe5ad7dae83a4217db3e8b'
  'src\LambLink.Protocol\ChzzkFollowerMarkerDiff.cs' = 'a3b5b986b5d932e3f92d8dba8d01934d4b9c39ae9f081fe0d61a1a0560b5d764'
  'src\LambLink.Companion\Program.cs' = 'ad1b316b99e43b80c5cf9dee9bcde4c134483edf4065031b6e627dcad13e0407'
  'src\LambLink.Companion\Chzzk\ChzzkApiClient.cs' = 'de54d18319d45922b606533db02803dddbebe6c0a710bd163467c21362296e0f'
  'src\LambLink.Companion\Chzzk\ChzzkRealtimeClient.cs' = '5a57fe90df6e36ede54e0d4d24f07d7c6557f2ef6454341e99cf9d6bae335893'
  'src\LambLink.Companion\Chzzk\Models.cs' = 'd5424be6e8dcab385b1a92424263b234a5aac0a6555ecad2467fdae53e2901ba'
  'src\LambLink.Companion\Chzzk\ProductionOAuth.cs' = 'd862084cd4d4239a1262c7b03ca0e9710efb3bf63870aa6c1daa4592713f52be'
  'src\LambLink.Companion\Cloud\AppearanceApiClient.cs' = '500a79bcb994452c7f6d4f7b694afbf69f8a778771c9194a7195384682c37aed'
  'src\LambLink.Companion\ViewerPage\ViewerPageShare.cs' = 'ad1417ea310d5e69786b808b710a5572043f89b9d8adc0b29718e50bdccb0b60'
  'src\LambLink.Companion\GameBridge\GameBridgeServer.cs' = '07bbfff0c50e8b0091f080ee4ad7b55c36263b6ed75c989f70ad1a471eb3ff31'
  'src\LambLink.Companion\Diagnostics\TeeTextWriter.cs' = 'a7e091c27028cadf0f21a5b844a37ae23eb0a44bb947eba3b8a42307c1860f47'
  'src\LambLink.Companion\Diagnostics\RollingFileTextWriter.cs' = 'e29998a28c482c747a734cca366bb5c85f7ac366ff429e7bc574c1eba7da6279'
  'src\LambLink.Companion\Diagnostics\DonationTraceRegistry.cs' = '6188d3760a8187e20daa522e7d6c7feb4bfa7cbf4250f6d2b03006bd9c042ecd'
  'src\LambLink.Companion\Diagnostics\DiagnosticPrivacy.cs' = '8385ef51e2158900925ec77ce865ead5633c870bd784f5bcfb137c3f8c1bbef7'
  'src\LambLink.Companion\Diagnostics\SupportBundleService.cs' = '19cab39b0ff77530e79f5de178a7bf666e68b7a3730e05f6043f53603e1fba65'
  'src\LambLink.Companion\Storage\DonationDeliveryRepository.cs' = '9fcb10a2b0d9fbb2a6e03112e6b274bbf102b4b3626a679435570eb5b2a16108'
  'src\LambLink.Companion\Storage\ViewerFollowerRepository.cs' = 'e10fc3955f24a2e21f2ce7839b2176a2b6052bb6953bf04f1419c936ca4ee607'
  'src\LambLink.Companion\Appearance\AppearanceStore.cs' = '14ff36135eb44a0b4f7a3a067bf604cef407316b7d303c475c26e98ee40d38bb'
  'src\LambLink.Companion\Overlay\RaffleOverlayServer.cs' = '1dd328315954b5b8d14267b9abbb7c2d25add54060b86f46253d7299a92799c7'
  'src\LambLink.Companion\LambLink.Companion.csproj' = '5f6bb1d3d0c508785a009d0ac8cb78b806254d79c048d067808595daba5b63c8'
  'src\LambLink.Companion\Configuration\CompanionLaunchProfile.cs' = '0ac9ef1eac171676f09f956b8579649d47d24b4603cabad2062a3bbaedad0870'
  'src\LambLink.Companion\Configuration\LegacyDataMigration.cs' = '5da7837cd88fafd1ed17eae0ee512238ee9d7245c128aea5ba2e2d9346f75921'
  'src\LambLink.Installer\Program.cs' = '67746963a10f399df2b0ba0e28bc22324ea47edfaedf1ca80997cdf67adcc115'
  'src\LambLink.Installer\LambLink.Installer.csproj' = 'fdb41202794ec4a63c121da2e51266d5ec08fb1430edcd5506790a7e635c089d'
  'installer\installer-manifest.template.json' = 'ed90593435bad77d07e5d90751cf7f719eb1aaf44b4841b7afe6abc1ddbf6109'
  'prepare-installer-manifest.ps1' = '12c46525d00049564abd6594def06f6e943e479d4d5d4eb228931bddc6cb16ea'
  'build-distribution.ps1' = '54fc042ff301efc0ee9ebd42bea0482098c21f3cdd72a46fc3d818149756064f'
  'aws\scripts\deploy-release-staging.ps1' = 'f57a468519bdf4846c0a4fb8c2f53be5f0cc50275e940554969982f388b4dcf1'
  'aws\scripts\install-release-staging.ps1' = 'ecd3287233a41c4f30e560f630d9d91971c8943de4d7ce2b5d245f6c7c248370'
  'aws\scripts\run-staging-companion.ps1' = 'dad9c5fc5a0550dd4ddfde42e319165cc361ab2886bfbde56b33082c08d653c2'
  'aws\scripts\deploy-production.ps1' = '12defcbf792f7528e1cd99c284cb0c5241db0eab4ac8d16edf8e67840f0428ac'
  'aws\scripts\deploy-release-production.ps1' = '66c9ee3579a47daf988b5e020d02c82cee21ee26490e6c9cca0845f5407a4793'
  'aws\frontend\index.html' = '2ab5c7475e67829a616ef6cc4cebc12b9f7488db10dd2dd6b6911047f9c7da29'
  'aws\template.yaml' = '0177db67b8bd6e90c57f3127b81dcb82eec3d0fb2536f6756ff549b217ce5e17'
  'tests\brand\test-brand-migration.ps1' = '1dde99dc23150504f467242045ea416b28519dde94c0e3135383efd6d8b342ea'
  'DISTRIBUTION-1.0.0.md' = 'b9bbb7ded34da5f747b004249a19ca912a2dad9af699fc77ce9f234a508b4411'
}
foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical v1.0.0 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    # Git for Windows commonly checks text files out as CRLF when core.autocrlf=true,
    # while the reviewed release hashes were recorded from the repository's LF blobs.
    # Normalize only CRLF line endings and hash the UTF-8 bytes again; any semantic
    # source change still fails the identity check.
    $sourceBytes = [System.IO.File]::ReadAllBytes($sourcePath)
    $sourceText = [System.Text.Encoding]::UTF8.GetString($sourceBytes)
    if ($sourceText.Length -gt 0 -and $sourceText[0] -eq [char]0xFEFF) {
      $sourceText = $sourceText.Substring(1)
    }
    $normalizedBytes = [System.Text.Encoding]::UTF8.GetBytes($sourceText.Replace("`r`n", "`n"))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
      $actualHash = ([System.BitConverter]::ToString($sha256.ComputeHash($normalizedBytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
      $sha256.Dispose()
    }
  }
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical v1.0.0 source does not match the reviewed version: $relativePath"
  }
}
Write-Host '[VERIFY] Critical v1.0.0 source hashes OK.'

Clear-CompilerOutputs

Write-Host '[1/9] Validating Korean font patch assets...'
if (-not (Test-Path $fontDll)) { throw "Missing: $fontDll" }
if (-not (Test-Path $fontBundle)) { throw "Missing: $fontBundle" }
$expectedFontDllSha256 = 'f51607271da49cbce4085ae30423aa133b7dc20bb374c16d919a95a0833036d8'
$expectedFontBundleSha256 = 'd711731544f49c59423715885e4466b75a135767515ab3d3ae4de58df4123a8f'
if ((Get-FileHash $fontDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedFontDllSha256) { throw 'Unexpected COTL_KoreanFontFix.dll' }
if ((Get-FileHash $fontBundle -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedFontBundleSha256) { throw 'Unexpected koreanfont.bundle' }
Write-Host '[ASSET] Korean Font Fix 4.2.1 verified.'

Write-Host '[2/9] Cleaning dist/release-hosting component artifacts...'
Remove-DirectoryTree $dist
New-Item -ItemType Directory -Force -Path $companionOut, $pluginOut, $hosting | Out-Null
foreach ($f in @($fontPackageName,"LambLink-Mod-$release.zip","LambLink-Companion-$release-win-x64.zip","LambLink-Setup-$release.exe","installer-manifest-$release.json")) {
  $p = Join-Path $hosting $f; if (Test-Path $p) { Remove-Item $p -Force }
}

Write-Host '[3/9] Restore...'
dotnet restore (Join-Path $root 'LambLink.sln')
Assert-NativeSuccess 'Release restore'

Write-Host '[4/9] Building game mod...'
dotnet build (Join-Path $root 'src\LambLink.Mod\LambLink.Mod.csproj') -c Release --no-restore
Assert-NativeSuccess 'Release Mod build'
$modBin = Join-Path $root 'src\LambLink.Mod\bin\Release'
Copy-Item (Join-Path $modBin 'LambLink.Mod.dll') $pluginOut -Force
Copy-Item (Join-Path $modBin 'LambLink.Protocol.dll') $pluginOut -Force

$modDll = Join-Path $pluginOut 'LambLink.Mod.dll'
$modBytes = [System.IO.File]::ReadAllBytes($modDll)
function Test-ByteSequence([byte[]]$Haystack, [byte[]]$Needle) {
  if ($Needle.Length -eq 0 -or $Haystack.Length -lt $Needle.Length) { return $false }
  if ($null -eq $script:ByteSequenceTextCache) {
    $script:ByteSequenceTextCache = @{}
  }
  $cacheKey = "{0}:{1}" -f [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($Haystack), $Haystack.Length
  if (-not $script:ByteSequenceTextCache.ContainsKey($cacheKey)) {
    # Latin-1 is a lossless one-byte-to-one-character projection. String.IndexOf
    # then performs the binary search in optimized .NET code instead of an
    # interpreted PowerShell loop over a 100+ MB single-file executable.
    $script:ByteSequenceTextCache[$cacheKey] = [Text.Encoding]::GetEncoding(28591).GetString($Haystack)
  }
  $needleText = [Text.Encoding]::GetEncoding(28591).GetString($Needle)
  return $script:ByteSequenceTextCache[$cacheKey].IndexOf($needleText, [StringComparison]::Ordinal) -ge 0
}
$buildTag = 'v1.0.0-production'
$hasBuildTag = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($buildTag))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($buildTag)))
if (-not $hasBuildTag) {
  throw 'Built mod DLL does not contain the v1.0.0 build tag. Refusing to package a stale DLL.'
}
foreach ($marker in @('io.github.xhayper.COTL_API', 'RAFFLE_ROUND_CLOSED', '[NAMEPLATE][PATCH-VERIFY]', '[NAMEPLATE][INLINE-APPLIED]', '[NAMEPLATE][TARGETED-REFRESH]', 'CACHE-HIT', '[IDENTITY-COMMIT]', 'CHZZK nameplate marker dropped', '<color=#00C471>Chzzk</color> ')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing required v1.0.0 marker: $marker" }
}
foreach ($forbidden in @('[NAMEPLATE][IDENTITY-REPAIRED]', '[FOLLOWER-MARKER][IDENTITY-REPAIRED]')) {
  $hasForbidden = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
                  (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($hasForbidden) { throw "Built Mod contains forbidden ID-only identity repair marker: $forbidden" }
}
foreach ($marker in @('[DONATION][RX]', '[DONATION][APPLIED]', '[DONATION][RESULT-TX]', '[DONATION][QUEUE][ENQUEUED]', '[DONATION][GATE][STATE]', '[DONATION][STORY-HOOK][CAPABILITY]', '[DONATION][BUFF-GROUP]', 'sharedStartIn=', 'DONATION_RUNTIME_STATE', 'stage=')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing v1.0.0 donation diagnostic marker: $marker" }
}
foreach ($marker in @('[DONATION][RECEIPT][UNCERTAIN]', '[DONATION][RECEIPT][RECOVERY]', 'automatic replay blocked')) {
  $hasMarker = (Test-ByteSequence $modBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $modBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built mod DLL is missing durable donation receipt marker: $marker" }
}
Write-Host "[VERIFY] v1.0.0 mod build tag and donation diagnostics found; SHA-256=$((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"

Write-Host '[5/9] Building and validating Companion diagnostics...'
$companionProject = Join-Path $root 'src\LambLink.Companion\LambLink.Companion.csproj'
dotnet build $companionProject -c Release --no-restore
Assert-NativeSuccess 'Release Companion validation build'
$companionValidationDll = Join-Path $root 'src\LambLink.Companion\bin\Release\net8.0\LambLink.Companion.dll'
if (-not (Test-Path $companionValidationDll)) { throw "Companion validation assembly was not produced: $companionValidationDll" }
$companionValidationBytes = [System.IO.File]::ReadAllBytes($companionValidationDll)
foreach ($forbidden in @('[FOLLOWER-MIGRATION][RC26-RESTORED]', 'name drift retained for repair')) {
  $hasForbidden = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
                  (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($hasForbidden) { throw "Built Companion contains forbidden unsaved-result recovery marker: $forbidden" }
}
foreach ($marker in @('COTL_STAGING_MODE', 'COTL_STAGING_DATA_DIR', 'LambLink-Staging', 'RELEASE / CHZZK LIVE / STAGING', '시청자 외형 설정 페이지 (Staging).url', 'follower-states?saveId=', '[FOLLOWER-STATE][HYDRATED]', 'Empty marker sync is suppressed.')) {
  $hasMarker = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built Companion validation assembly is missing staging isolation marker: $marker" }
}
foreach ($marker in @('[DONATION][TERMINAL][ACK-TIMEOUT]', '[DONATION][GATE][RX]', 'pausedWhileModGateBlocked=true', '[OVERLAY][BUFF-TIMER][PAUSED]', '[OVERLAY][BUFF-GROUP]', '[OVERLAY][DOCUMENT] version=', '[OVERLAY][STALE-DOCUMENT]', '[OVERLAY][CLIENT-DOCUMENT]', '[OVERLAY][CLIENT-LAYOUT]', 'v1.0.0-overlay-document-v1', '#donationWrap{position:fixed;left:18px;right:auto;top:18px;width:min(480px', '#donationWrap .panel{width:100%;box-sizing:border-box}', '<div id="donationWrap"><div class="panel" id="donationPanel"></div></div>', '#buffs{position:fixed;left:18px;right:auto;top:18px', 'direction:ltr', 'justify-content:flex-start', '/overlay/client-layout?', 'location.replace(', 'OVERLAY_DOC_CURRENT=', '[OVERLAY][DONATION-QUEUE][ENQUEUED]', '[OVERLAY][DONATION-QUEUE][DISPLAY]', '[OVERLAY][DONATION-QUEUE][COMPLETED]', 'DONATION_GATE=', 'DONATION_OUTBOX=', '[DONATION][OUTBOX][RECOVERY]', 'donation outbox and Mod receipt contents', '[SUPPORT][READY]', 'companion-1.0.0.log', 'fallback snapshot every', '[STAGING TEST] 운영 환경이 아닙니다.', 'installed-launch-profile', 'companion-launch-profile.json')) {
  $hasMarker = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built Companion validation assembly is missing v1.0.0 diagnostic marker: $marker" }
}
foreach ($forbiddenMarker in @(
  'RELEASE_TEST_TOOLS enabled',
  'dev spawn ',
  'dev join ',
  'dev donation ',
  'AWS_PROFILE',
  'cotl-dev',
  'aws sso login',
  'Failed to start AWS CLI.'
)) {
  $hasForbidden = (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::UTF8.GetBytes($forbiddenMarker))) -or
                  (Test-ByteSequence $companionValidationBytes ([System.Text.Encoding]::Unicode.GetBytes($forbiddenMarker)))
  if ($hasForbidden) { throw "Release Companion unexpectedly contains development-only code: $forbiddenMarker" }
}
Write-Host '[VERIFY] v1.0.0 release Companion excludes test commands and local AWS CLI/SSO credential code.'
Write-Host '[VERIFY] v1.0.0 support, environment isolation, and donation markers found in compiled Companion assembly.'

Write-Host '[5/9] Publishing Companion self-contained single-file...'
dotnet publish $companionProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $companionOut
Assert-NativeSuccess 'Release Companion publish'
$companionExe = Join-Path $companionOut 'LambLink.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($companionVersion.FileVersion -ne $fileVersion) { throw "Unexpected Companion file version: $($companionVersion.FileVersion)" }
Write-Host "[VERIFY] v1.0.0 Companion EXE exists and file version is $fileVersion."

Write-Host '[6/9] Creating normalized downloadable component ZIPs...'
$temp = Join-Path $distRoot '_component-build'
Remove-DirectoryTree $temp
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$modPkg = Join-Path $temp 'mod\BepInEx\plugins\LambLink'
New-Item -ItemType Directory -Force -Path $modPkg | Out-Null
Copy-Item (Join-Path $pluginOut '*') $modPkg -Force
Compress-Archive -Path (Join-Path $temp 'mod\*') -DestinationPath (Join-Path $hosting "LambLink-Mod-$release.zip") -CompressionLevel Optimal

$fontPkg = Join-Path $temp 'font\BepInEx\plugins\COTL_KoreanFontFix'
New-Item -ItemType Directory -Force -Path $fontPkg | Out-Null
Copy-Item $fontDll (Join-Path $fontPkg 'COTL_KoreanFontFix.dll') -Force
Copy-Item $fontBundle (Join-Path $fontPkg 'koreanfont.bundle') -Force
Compress-Archive -Path (Join-Path $temp 'font\*') -DestinationPath (Join-Path $hosting $fontPackageName) -CompressionLevel Optimal

Compress-Archive -Path (Join-Path $companionOut '*') -DestinationPath (Join-Path $hosting "LambLink-Companion-$release-win-x64.zip") -CompressionLevel Optimal

Write-Host '[7/9] Publishing single-file GUI installer...'
$installerPublish = Join-Path $distRoot '_installer-publish'
Remove-DirectoryTree $installerPublish
dotnet publish (Join-Path $root 'src\LambLink.Installer\LambLink.Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $installerPublish
Assert-NativeSuccess 'Release Installer publish'
$installerExe = Join-Path $installerPublish 'LambLink.Installer.exe'
if (-not (Test-Path $installerExe)) { throw "Installer EXE was not produced: $installerExe" }
$installerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installerExe)
if ($installerVersion.FileVersion -ne $fileVersion) { throw "Unexpected Installer file version: $($installerVersion.FileVersion)" }
if (-not $installerVersion.ProductVersion.StartsWith($release, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Unexpected Installer product version: $($installerVersion.ProductVersion)"
}
$installerBytes = [System.IO.File]::ReadAllBytes($installerExe)
foreach ($marker in @('STAGING TEST', 'PRODUCTION', 'companion-launch-profile.json', 'LambLink-Staging', 'STAGING Companion 실행')) {
  $hasMarker = (Test-ByteSequence $installerBytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
               (Test-ByteSequence $installerBytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $hasMarker) { throw "Built Installer is missing v1.0.0 environment-isolation marker: $marker" }
}
Write-Host '[VERIFY] v1.0.0 Installer contains distinct staging/production install markers.'
Copy-Item $installerExe (Join-Path $hosting "LambLink-Setup-$release.exe") -Force

Write-Host '[8/9] Creating legacy test ZIP + release docs...'
Copy-Item (Join-Path $root 'RELEASE-README.md') $dist -Force
$zip = Join-Path $distRoot "LambLink-v$release-win-x64-legacy.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '[9/9] Finished local build.'
Write-Host "Installer EXE: $(Join-Path $hosting "LambLink-Setup-$release.exe")"
Write-Host ".\prepare-installer-manifest.ps1 generates installer-manifest-$release.json."
Write-Host "Recommended: run .\build-distribution.ps1 to validate and assemble the complete $release handoff bundle."
