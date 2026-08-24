$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj'
$dist = Join-Path $root 'dist\rc18-companion'

$criticalSources = @{
  'src\ChzzkOfTheLamb.Companion\Program.cs' = 'f5690acb740533d9604df78e6503283d485202a898f723c048d6c5d62ca2675d'
  'src\ChzzkOfTheLamb.Companion\GameBridge\GameBridgeServer.cs' = 'c3b7f7a0ed22594bc1f2ec4b8ffeafc1712180979522ea708701e64ddeb5bf17'
  'src\ChzzkOfTheLamb.Companion\Appearance\AppearanceStore.cs' = '6726689d6ffcef4c31d4064649fdc7be38c28d219fdf8eb7cb9db4afa75b893b'
  'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj' = 'e33d2cd5d6951938e0b76c33faea4e010643a265bb6713080ed67d70df9f77f5'
  'src\ChzzkOfTheLamb.Protocol\GameMessages.cs' = '2cbf4ef7e8c9427f006745b9737bf2c5f63cf58dca26463b404bc316d162e1b2'
}

foreach ($relativePath in $criticalSources.Keys) {
  $sourcePath = Join-Path $root $relativePath
  if (-not (Test-Path $sourcePath)) { throw "Missing critical RC18 source: $relativePath" }
  $actualHash = (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $criticalSources[$relativePath]) {
    throw "Critical RC18 source does not match the reviewed version: $relativePath"
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
if ($version.FileVersion -ne '1.0.0.18') {
  throw "Unexpected Companion file version: $($version.FileVersion)"
}

Write-Host '[OK] RC18 Companion built and verified.'
Write-Host "Output: $companionExe"
Write-Host "SHA-256: $((Get-FileHash $companionExe -Algorithm SHA256).Hash.ToLowerInvariant())"
