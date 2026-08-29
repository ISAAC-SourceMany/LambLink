$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\LambLink.Mod\LambLink.Mod.csproj'
$dist = Join-Path $root 'dist\v1.0.0-plugin-test'

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
    # Windows PowerShell 5.1 can partially remove a long tree and then report
    # DirectoryNotFoundException for a child it has already deleted.
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
  'src\LambLink.Mod\LambLink.Mod.csproj' = '4e4912f4e62e273a3a4175b3d000ead95874193f29e545ea4c373805bc77ccf6'
  'src\LambLink.Mod\Plugin.cs' = '594f7159efbcb2cef29615329fc49f47960ba4f0f0e24b6916be5a7d06fe4d41'
  'src\LambLink.Mod\BridgeRuntimeHost.cs' = '499b5a9b14ff2fab6bdb84a9304550dd2d19c079ca50e5a6edbdf08894a66279'
  'src\LambLink.Mod\Network\ModBridgeClient.cs' = 'a355fecbbabe76fa69d5bf089f48a28d9bc60da5ad4233d794c68f18c346b63c'
  'src\LambLink.Mod\Game\IndoctrinationRafflePatch.cs' = '9804b3e1d156bab2981f85ff90a52733b83b6f8719ee48309b7f84a88788fee8'
  'src\LambLink.Mod\Game\FollowerNameplatePatch.cs' = 'fe7d7a9503b982316e3f94d0f02caa939d9876fccc9c5b543e8d54a11e341a39'
  'src\LambLink.Mod\Game\FollowerService.cs' = '4b9313e13d5bd398f633018ee9830ae1731cbdd03d8b63dcff7c6f4268fe9b09'
  'src\LambLink.Mod\Game\FollowerAppearanceService.cs' = '7da41f06f7e22cb0beefaab61c0ec346a8cb533246e1ceb94c21c61df1c45856'
  'src\LambLink.Mod\Game\GameSaveService.cs' = '9114554d1a1d84692e84470708766ee0fb52a1587c04cae1312d1b984a7a6b28'
  'src\LambLink.Mod\Game\DonationEffectService.cs' = 'ea8776182850a7c5598d7e7d059ff85963b50f4d3c10316ce58aa3cbdc43ed60'
  'src\LambLink.Mod\Game\DonationGameplayGate.cs' = '9b1f8cb026b98f4852cab761ab39075d5cce8167debb35a2e9deb69d1a2c17ca'
  'src\LambLink.Mod\Game\DonationStoryLifecycle.cs' = '4304de3eb5e27937992929253549798e5dd0d26ec3d72629af00808dda108da6'
  'src\LambLink.Mod\Game\DungeonDonationBuffs.cs' = '0945d15961880bcaa9c3d0dfec0cae2302a278f54b49ce41a0db2372b119f905'
  'src\LambLink.Protocol\GameMessages.cs' = '826b2d7429942cfdaaa910f415a54fa0083bb755a3fe5ad7dae83a4217db3e8b'
  'src\LambLink.Protocol\ChzzkFollowerMarkerDiff.cs' = 'a3b5b986b5d932e3f92d8dba8d01934d4b9c39ae9f081fe0d61a1a0560b5d764'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical v1.0.0 source: $relativePath" }
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
    throw "Critical v1.0.0 source does not match the reviewed version: $relativePath"
  }
}

Clear-CompilerOutputs
Remove-DirectoryTree $dist
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet restore $project
Assert-NativeSuccess 'Plugin restore'
dotnet build $project -c Release --no-restore
Assert-NativeSuccess 'Plugin build'

$bin = Join-Path $root 'src\LambLink.Mod\bin\Release'
$modSource = Join-Path $bin 'LambLink.Mod.dll'
$protocolSource = Join-Path $bin 'LambLink.Protocol.dll'
if (-not (Test-Path $modSource)) { throw "Plugin build reported success but output is missing: $modSource" }
if (-not (Test-Path $protocolSource)) { throw "Plugin build reported success but output is missing: $protocolSource" }
Copy-Item $modSource $dist -Force
Copy-Item $protocolSource $dist -Force

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

$modDll = Join-Path $dist 'LambLink.Mod.dll'
$bytes = [System.IO.File]::ReadAllBytes($modDll)
$tag = 'v1.0.0-production'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'v1.0.0 build tag missing from compiled DLL; stale build rejected.' }

foreach ($marker in @('io.github.xhayper.COTL_API', 'RAFFLE_ROUND_CLOSED', '[NAMEPLATE][PATCH-VERIFY]', '[NAMEPLATE][INLINE-APPLIED]', '[NAMEPLATE][TARGETED-REFRESH]', 'CACHE-HIT', '[IDENTITY-COMMIT]', 'CHZZK nameplate marker dropped', '<color=#00C471>Chzzk</color> ')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $found) { throw "v1.0.0 compiled Mod is missing required marker: $marker" }
}

foreach ($forbidden in @('[NAMEPLATE][IDENTITY-REPAIRED]', '[FOLLOWER-MARKER][IDENTITY-REPAIRED]')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($found) { throw "v1.0.0 compiled Mod contains forbidden ID-only identity repair marker: $forbidden" }
}

foreach ($marker in @('[DONATION][RX]', '[DONATION][APPLIED]', '[DONATION][RESULT-TX]', '[DONATION][QUEUE][ENQUEUED]', '[DONATION][GATE][STATE]', '[DONATION][STORY-HOOK][CAPABILITY]', '[DONATION][BUFF-GROUP]', 'sharedStartIn=', 'DONATION_RUNTIME_STATE', 'stage=')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $found) { throw "v1.0.0 compiled Mod is missing donation diagnostic marker: $marker" }
}

Write-Host '[OK] v1.0.0 live-donation diagnostic Mod built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
