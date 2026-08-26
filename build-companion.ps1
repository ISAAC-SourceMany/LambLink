$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
$dist = Join-Path $root 'dist\rc33-companion'

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
  'src\ChzzkOfTheLamb.Companion\Program.cs' = '368573547fa75f35106d8b5707c224d878c1b4e25211b0d8eb404948012b29f4'
  'src\ChzzkOfTheLamb.Companion\Chzzk\ChzzkRealtimeClient.cs' = '09ce1e58e9308f7fecfb320635b3e5b32f6c2c0a9414d0200594226df18a7c8a'
  'src\ChzzkOfTheLamb.Companion\ViewerPage\ViewerPageShare.cs' = '2e9041cf4209e76f258c7a0cdab0847431f4affef002bd03b1ee37efef36692a'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = '6199cf43fea8adbcb9b166f31370975f2ac954af3834bbdf3866142e55fe8da9'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\TeeTextWriter.cs' = '3edd24b12f8aaac9a1de768be84d0d6711c1b30c28a8bff39507912ffffcd305'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\RollingFileTextWriter.cs' = 'c44b021eda8285598fbfff015881f3406fef8c84545d08f78db757a70b81793a'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\DonationTraceRegistry.cs' = 'c56b6c0f3970917911b1ef21eae00c543b4783eaeb1d1f445ec2ccf9da9061e8'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\DiagnosticPrivacy.cs' = 'c9afc180f204d35c8175e1be3c37167686add4e8c43b8d737e0c7a00ce406f36'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\SupportBundleService.cs' = 'd17c653e9698dd32157ac88d28bf243aa8df45ff616dbb1df201412b33248ba3'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\Overlay\RaffleOverlayServer.cs' = '739e3d10cedf5f809426151193eb4eda1e9bb8d40e851e81ac332ca70b8158c7'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = '5dad28280d5ab6c18a34e7e6f054a93fd363c1d5cd6db7d05e69cecadf20b972'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '55312e81b19340d18188ad0cf6efbcb7a06f6bef0ccb243f1ae568172d401a64'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC33 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC33 source does not match the reviewed version: $relativePath"
  }
}

Clear-CompilerOutputs
Remove-DirectoryTree $dist
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet restore $project -p:EnableRcTestTools=true
Assert-NativeSuccess 'Companion restore'

dotnet build $project -c Release --no-restore -p:EnableRcTestTools=true
Assert-NativeSuccess 'Companion validation build'

$validationAssembly = Join-Path $root 'src\ChzzkOfTheLamb.Companion\bin\Release\net8.0\ChzzkOfTheLamb.Companion.dll'
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
  if ($found) { throw "RC33 Companion contains forbidden unsaved-result recovery marker: $forbidden" }
}
foreach ($required in @('RC33_TEST_TOOLS enabled', 'dev donation', '[DONATION][TERMINAL][ACK-TIMEOUT]', '[DONATION][GATE][RX]', 'pausedWhileModGateBlocked=true', '[OVERLAY][BUFF-TIMER][PAUSED]', '[OVERLAY][BUFF-GROUP]', 'donationView', 'position:fixed;left:18px', '[OVERLAY][DONATION-QUEUE][ENQUEUED]', '[OVERLAY][DONATION-QUEUE][DISPLAY]', '[OVERLAY][DONATION-QUEUE][COMPLETED]', 'DONATION_GATE=', '[SUPPORT][READY]', 'companion-rc33.log')) {
  $found = (Test-ByteSequence $validationBytes ([System.Text.Encoding]::UTF8.GetBytes($required))) -or
           (Test-ByteSequence $validationBytes ([System.Text.Encoding]::Unicode.GetBytes($required)))
  if (-not $found) { throw "RC33 Companion validation assembly is missing diagnostic marker: $required" }
}
Write-Host '[VERIFY] RC33 diagnostic markers found in compiled Companion assembly.'

dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:EnableRcTestTools=true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $dist
Assert-NativeSuccess 'Companion publish'

$companionExe = Join-Path $dist 'ChzzkOfTheLamb.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($version.FileVersion -ne '1.0.0.33') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC33 Companion diagnostics build verified with RC33_TEST_TOOLS.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
