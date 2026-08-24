$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
$dist = Join-Path $root 'dist\rc21-companion'

$criticalSources = @{
  'src\ChzzkOfTheLamb.Companion\Program.cs' = '0400585c337f006b6bc5e8e89e24383d36677ad23d6bde2160656ca5e5b81543'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = '7f5070e83242227d5baee5c64c6565f6f0b8020d54ff280a2f96daea5eb47fdd'
  'src\ChzzkOfTheLamb.Companion\Diagnostics\TeeTextWriter.cs' = '3edd24b12f8aaac9a1de768be84d0d6711c1b30c28a8bff39507912ffffcd305'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = 'ee53443a76a2fe34ff22a09f8f086a6fb057eb2e2c7942f7b2780f0eabfdd79d'
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
if ($version.FileVersion -ne '1.0.0.21') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC21 Companion built and verified.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
