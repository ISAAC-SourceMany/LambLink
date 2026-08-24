$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj'
$dist = Join-Path $root 'dist\rc19-plugin'

$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'a93c1d5edf62e97012d440610bbfad186bbe30503af62dd0016e474e72c703d0'
  'src\ChzzkOfTheLamb.Mod\Network\ModBridgeClient.cs' = '2c1162ac922e54cd38da6b6d0406c4c31febcb013a1728208461819b65a59f48'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '81260035a8f74e613b414576d1065373c76fcdccca000a790097fa4047b2f9cb'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '1c0ee08c2ce668ebb15cd41cf757bd6b4d28cc3e1ee37bf3e4a5ac1a0f8ba02f'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '2cbf4ef7e8c9427f006745b9737bf2c5f63cf58dca26463b404bc316d162e1b2'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC19 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC19 source does not match the reviewed version: $relativePath"
  }
}

Get-ChildItem -Path (Join-Path $root 'src') -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin', 'obj') } |
  Remove-Item -Recurse -Force
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet restore $project
dotnet build $project -c Release --no-restore

$bin = Join-Path $root 'src\ChzzkOfTheLamb.Mod\bin\Release'
Copy-Item (Join-Path $bin 'ChzzkOfTheLamb.Mod.dll') $dist -Force
Copy-Item (Join-Path $bin 'ChzzkOfTheLamb.Protocol.dll') $dist -Force

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
$tag = 'rc19-safe-game-status-sync'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'RC19 build tag missing from compiled DLL; stale build rejected.' }

Write-Host '[OK] RC19 safe game-status sync + automatic appearance unlock plugin built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
