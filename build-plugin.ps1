$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj'
$dist = Join-Path $root 'dist\rc31-plugin'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

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

dotnet restore $project
Assert-NativeSuccess 'Plugin restore'
dotnet build $project -c Release --no-restore
Assert-NativeSuccess 'Plugin build'

$bin = Join-Path $root 'src\ChzzkOfTheLamb.Mod\bin\Release'
$modSource = Join-Path $bin 'ChzzkOfTheLamb.Mod.dll'
$protocolSource = Join-Path $bin 'ChzzkOfTheLamb.Protocol.dll'
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

$modDll = Join-Path $dist 'ChzzkOfTheLamb.Mod.dll'
$bytes = [System.IO.File]::ReadAllBytes($modDll)
$tag = 'rc31-donation-safe-runtime-paused-buffs'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'RC31 build tag missing from compiled DLL; stale build rejected.' }

foreach ($marker in @('io.github.xhayper.COTL_API', 'RAFFLE_ROUND_CLOSED', '[NAMEPLATE][PATCH-VERIFY]', '[NAMEPLATE][INLINE-APPLIED]', '[IDENTITY-COMMIT]', 'CHZZK nameplate marker dropped', '<color=#00C471>Chzzk</color> ')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $found) { throw "RC31 compiled Mod is missing required marker: $marker" }
}

foreach ($forbidden in @('[NAMEPLATE][IDENTITY-REPAIRED]', '[FOLLOWER-MARKER][IDENTITY-REPAIRED]')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($forbidden))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($forbidden)))
  if ($found) { throw "RC31 compiled Mod contains forbidden ID-only identity repair marker: $forbidden" }
}

foreach ($marker in @('[DONATION][RX]', '[DONATION][APPLIED]', '[DONATION][RESULT-TX]', '[DONATION][QUEUE][ENQUEUED]', '[DONATION][GATE][STATE]', '[DONATION][STORY-HOOK][CAPABILITY]', 'DONATION_RUNTIME_STATE', 'stage=')) {
  $found = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($marker))) -or
           (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($marker)))
  if (-not $found) { throw "RC31 compiled Mod is missing donation diagnostic marker: $marker" }
}

Write-Host '[OK] RC31 live-donation diagnostic Mod built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
