$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
$dist = Join-Path $root 'dist\rc31-companion'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

$criticalSources = @{
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
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '55312e81b19340d18188ad0cf6efbcb7a06f6bef0ccb243f1ae568172d401a64'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC31 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC31 source does not match the reviewed version: $relativePath"
  }
}

Get-ChildItem -Path (Join-Path $root 'src') -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin', 'obj') } |
  Remove-Item -Recurse -Force
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
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
  if ($found) { throw "RC31 Companion contains forbidden unsaved-result recovery marker: $forbidden" }
}
foreach ($required in @('RC31_TEST_TOOLS enabled', 'dev donation', '[DONATION][TERMINAL][ACK-TIMEOUT]', '[DONATION][GATE][RX]', 'pausedWhileModGateBlocked=true', '[OVERLAY][BUFF-TIMER][PAUSED]', 'DONATION_GATE=', '[SUPPORT][READY]', 'companion-rc31.log')) {
  $found = (Test-ByteSequence $validationBytes ([System.Text.Encoding]::UTF8.GetBytes($required))) -or
           (Test-ByteSequence $validationBytes ([System.Text.Encoding]::Unicode.GetBytes($required)))
  if (-not $found) { throw "RC31 Companion validation assembly is missing diagnostic marker: $required" }
}
Write-Host '[VERIFY] RC31 diagnostic markers found in compiled Companion assembly.'

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
if ($version.FileVersion -ne '1.0.0.31') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC31 Companion diagnostics build verified with RC31_TEST_TOOLS.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
