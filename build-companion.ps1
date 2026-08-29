$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\LambLink.Companion\LambLink.Companion.csproj'
$dist = Join-Path $root 'dist\rc39-companion'

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

$criticalSources = @{
  'src\LambLink.Companion\Program.cs' = 'd803dee00d01575a5dca7bcf078fd3cd37cdf38cae05cfcb12b4002727489b6f'
  'src\LambLink.Companion\Chzzk\ChzzkRealtimeClient.cs' = '5a57fe90df6e36ede54e0d4d24f07d7c6557f2ef6454341e99cf9d6bae335893'
  'src\LambLink.Companion\ViewerPage\ViewerPageShare.cs' = 'ad1417ea310d5e69786b808b710a5572043f89b9d8adc0b29718e50bdccb0b60'
  'src\LambLink.Companion\GameBridge\GameBridgeServer.cs' = '07bbfff0c50e8b0091f080ee4ad7b55c36263b6ed75c989f70ad1a471eb3ff31'
  'src\LambLink.Companion\Diagnostics\TeeTextWriter.cs' = 'a7e091c27028cadf0f21a5b844a37ae23eb0a44bb947eba3b8a42307c1860f47'
  'src\LambLink.Companion\Diagnostics\RollingFileTextWriter.cs' = 'e29998a28c482c747a734cca366bb5c85f7ac366ff429e7bc574c1eba7da6279'
  'src\LambLink.Companion\Diagnostics\DonationTraceRegistry.cs' = '6188d3760a8187e20daa522e7d6c7feb4bfa7cbf4250f6d2b03006bd9c042ecd'
  'src\LambLink.Companion\Diagnostics\DiagnosticPrivacy.cs' = '8385ef51e2158900925ec77ce865ead5633c870bd784f5bcfb137c3f8c1bbef7'
  'src\LambLink.Companion\Diagnostics\SupportBundleService.cs' = '19cab39b0ff77530e79f5de178a7bf666e68b7a3730e05f6043f53603e1fba65'
  'src\LambLink.Companion\Appearance\AppearanceStore.cs' = '14ff36135eb44a0b4f7a3a067bf604cef407316b7d303c475c26e98ee40d38bb'
  'src\LambLink.Companion\Overlay\RaffleOverlayServer.cs' = 'df6fd46ee930dc23dd57345163fd513b9be074d30123d44c738d7f3d29163219'
  'src\LambLink.Companion\LambLink.Companion.csproj' = '93a17db6abe70e0d907ca20fc9dd5552b27f37d77d19f7f11503621e60da129e'
  'src\LambLink.Companion\Configuration\CompanionLaunchProfile.cs' = '0ac9ef1eac171676f09f956b8579649d47d24b4603cabad2062a3bbaedad0870'
  'src\LambLink.Companion\Configuration\LegacyDataMigration.cs' = '5da7837cd88fafd1ed17eae0ee512238ee9d7245c128aea5ba2e2d9346f75921'
  'src\LambLink.Protocol\GameMessages.cs' = '826b2d7429942cfdaaa910f415a54fa0083bb755a3fe5ad7dae83a4217db3e8b'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC39 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    $sourceText = [System.IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
      $actualHash = ([System.BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($sourceText)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
      $sha256.Dispose()
    }
  }
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC39 source does not match the reviewed version: $relativePath"
  }
}

Clear-CompilerOutputs
Remove-DirectoryTree $dist
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet restore $project -p:EnableRcTestTools=true
Assert-NativeSuccess 'Companion restore'

dotnet build $project -c Release --no-restore -p:EnableRcTestTools=true
Assert-NativeSuccess 'Companion validation build'

$validationAssembly = Join-Path $root 'src\LambLink.Companion\bin\Release\net8.0\LambLink.Companion.dll'
if (-not (Test-Path $validationAssembly)) {
  throw "Companion validation assembly was not produced: $validationAssembly"
}

$validationBytes = [System.IO.File]::ReadAllBytes($validationAssembly)
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
foreach ($forbidden in @('[FOLLOWER-MIGRATION][RC26-RESTORED]', 'name drift retained for repair')) {
  $found = (Test-ByteSequence $validationBytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
           (Test-ByteSequence $validationBytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($found) { throw "RC39 Companion contains forbidden unsaved-result recovery marker: $forbidden" }
}
foreach ($required in @('RC39_TEST_TOOLS enabled', 'dev donation', '[DONATION][TERMINAL][ACK-TIMEOUT]', '[DONATION][GATE][RX]', 'pausedWhileModGateBlocked=true', '[OVERLAY][BUFF-TIMER][PAUSED]', '[OVERLAY][BUFF-GROUP]', '[OVERLAY][DOCUMENT] version=', '[OVERLAY][STALE-DOCUMENT]', '[OVERLAY][CLIENT-DOCUMENT]', '[OVERLAY][CLIENT-LAYOUT]', 'rc39-overlay-document-v1', '#donationWrap{position:fixed;left:18px;right:auto;top:18px;width:min(480px', '#donationWrap .panel{width:100%;box-sizing:border-box}', '<div id="donationWrap"><div class="panel" id="donationPanel"></div></div>', '#buffs{position:fixed;left:18px;right:auto;top:18px', 'direction:ltr', 'justify-content:flex-start', '/overlay/client-layout?', 'location.replace(', 'OVERLAY_DOC_CURRENT=', '[OVERLAY][DONATION-QUEUE][ENQUEUED]', '[OVERLAY][DONATION-QUEUE][DISPLAY]', '[OVERLAY][DONATION-QUEUE][COMPLETED]', 'DONATION_GATE=', '[SUPPORT][READY]', 'companion-rc39.log', '[STAGING TEST] 운영 환경이 아닙니다.', 'installed-launch-profile', 'companion-launch-profile.json')) {
  $found = (Test-ByteSequence $validationBytes ([System.Text.Encoding]::UTF8.GetBytes($required))) -or
           (Test-ByteSequence $validationBytes ([System.Text.Encoding]::Unicode.GetBytes($required)))
  if (-not $found) { throw "RC39 Companion validation assembly is missing diagnostic marker: $required" }
}
Write-Host '[VERIFY] RC39 diagnostic markers found in compiled Companion assembly.'

dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:EnableRcTestTools=true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $dist
Assert-NativeSuccess 'Companion publish'

$companionExe = Join-Path $dist 'LambLink.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($version.FileVersion -ne '1.0.0.39') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC39 Companion diagnostics build verified with RC39_TEST_TOOLS.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
