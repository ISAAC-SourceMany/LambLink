$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj'
$dist = Join-Path $root 'dist\rc16-plugin'

$criticalSources = @{
  'src\ChzzkOfTheLamb.Mod\Plugin.cs' = 'afcf18a87f2ccd4195e1ccaa3033c1ee76996acce6a9de26831ec1efd6577a0c'
  'src\ChzzkOfTheLamb.Mod\Game\IndoctrinationRafflePatch.cs' = '1634cf8a2d480437145067f433cf271018e1a3b50c7e032c27ab2f360a3dd49b'
  'src\ChzzkOfTheLamb.Mod\Game\FollowerService.cs' = '1c0ee08c2ce668ebb15cd41cf757bd6b4d28cc3e1ee37bf3e4a5ac1a0f8ba02f'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '80d837a5e3190f3747ff2b83deaf0c38512cb86c1aad492b1262fd7b4d3c467d'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC16 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC16 source does not match the reviewed version: $relativePath"
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
$tag = 'rc16-dev10z-raffle-immediate-bridge'
$tagFound = (Test-ByteSequence $bytes ([System.Text.Encoding]::UTF8.GetBytes($tag))) -or
            (Test-ByteSequence $bytes ([System.Text.Encoding]::Unicode.GetBytes($tag)))
if (-not $tagFound) { throw 'RC16 build tag missing from compiled DLL; stale build rejected.' }

Write-Host '[OK] RC16 dev10z raffle + immediate bridge plugin built and verified.'
Write-Host "Output: $dist"
Write-Host "Mod SHA-256: $((Get-FileHash $modDll -Algorithm SHA256).Hash.ToLowerInvariant())"
