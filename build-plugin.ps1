$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj'
$dist = Join-Path $root 'dist\rc21-plugin'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'af6f54f5fbe4a0c818f9516d940aedad43f0b07a4ad6027994ef52ca598dc5c8'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = 'f4880b66af179cdb341abee543fd1d4e0c40a23d893cdd57ad7571b61cac7bbf'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '81260035a8f74e613b414576d1065373c76fcdccca000a790097fa4047b2f9cb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '6883d37210328c161ead327c6d4c9ff1576c96aa8793790872d4d8791f7a0ee9'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerAppearanceService.cs' = 'e840ef802b18b8c52155c01f63bf0e1d3bc8d69e2f433407f7c2d7eb3e08ddc4'
  'src\ChzzkOfTheLamb.Mod\Game\GameSaveService.cs' = '5b48b8ce1f0c50ae47a9e160ce4244ab9b3712c2cc96e8cc55678163ded72c5d'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = 'd64b622d34e9956dbdf953a1e36c8a0cf4b94be1ad0635a07f46c7ff0e180c1e'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC21 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC21 source does not match the reviewed version: $relativePath"
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
$tag = 'rc21-diagnostic-watchdog-fallback'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'RC21 build tag missing from compiled DLL; stale build rejected.' }

Write-Host '[OK] RC21 command-first dispatch + watchdog + cached status fallback plugin built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
