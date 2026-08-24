$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj'
$dist = Join-Path $root 'dist\rc22-plugin'

function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'c06129967e637abd53babe59e1248f51bf489562bb5a4feb98b3fd1bd9307521'
  'src\ChzzkOfTheLamb.Mod\BridgeRuntimeHost.cs' = 'd9722fc1c14faa2a04829333151e1c5506b8d95505a4abafe6801290c924a7a7'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = 'dbe786dbd6cfa0df9144c87820e696b5ec076a8ee9c3f5b018b358179ff15595'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '81260035a8f74e613b414576d1065373c76fcdccca000a790097fa4047b2f9cb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '6883d37210328c161ead327c6d4c9ff1576c96aa8793790872d4d8791f7a0ee9'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerAppearanceService.cs' = 'e840ef802b18b8c52155c01f63bf0e1d3bc8d69e2f433407f7c2d7eb3e08ddc4'
  'src\ChzzkOfTheLamb.Mod\Game\GameSaveService.cs' = '5b48b8ce1f0c50ae47a9e160ce4244ab9b3712c2cc96e8cc55678163ded72c5d'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = 'd64b622d34e9956dbdf953a1e36c8a0cf4b94be1ad0635a07f46c7ff0e180c1e'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC22 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC22 source does not match the reviewed version: $relativePath"
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
$tag = 'rc22-persistent-runtime-host'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'RC22 build tag missing from compiled DLL; stale build rejected.' }

Write-Host '[OK] RC22 persistent runtime host + reconnecting bridge + state/catalog diagnostics plugin built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
