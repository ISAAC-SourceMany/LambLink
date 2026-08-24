$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
$dist = Join-Path $root 'dist\rc17-companion'

$criticalSources = @{
  'src\ChzzkOfTheLamb.Companion\Program.cs' = '3f6b0b776df1d21bae6d71caf720105c1b48351f4d5233b9a27e583290839f7f'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = 'c3b7f7a0ed22594bc1f2ec4b8ffeafc1712180979522ea708701e64ddeb5bf17'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = 'f90c7dbb5d8c0cee0cbe835f11c4cbae93b556b4ad32ba2bea3ae0d035209be4'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '2cbf4ef7e8c9427f006745b9737bf2c5f63cf58dca26463b404bc316d162e1b2'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC17 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC17 source does not match the reviewed version: $relativePath"
  }
}

Get-ChildItem -Path (Join-Path $root 'src') -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin', 'obj') } |
  Remove-Item -Recurse -Force
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $dist

$companionExe = Join-Path $dist 'ChzzkOfTheLamb.Companion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion EXE was not produced: $companionExe" }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companionExe)
if ($version.FileVersion -ne '1.0.0.17') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC17 Companion built and verified.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
