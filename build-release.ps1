$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist\ChzzkOfTheLamb-v1.0.0'
$companionOut = Join-Path $dist 'Companion'
$pluginOut = Join-Path $dist 'Plugin'

Write-Host '[1/5] Cleaning dist...'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $companionOut, $pluginOut | Out-Null

Write-Host '[2/5] Restore...'
dotnet restore (Join-Path $root 'ChzzkOfTheLamb.sln')

Write-Host '[3/5] Building game mod...'
dotnet build (Join-Path $root 'src\ChzzkOfTheLamb.Mod\ChzzkOfTheLamb.Mod.csproj') -c Release --no-restore
$modBin = Join-Path $root 'src\ChzzkOfTheLamb.Mod\bin\Release'
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Mod.dll') $pluginOut -Force
Copy-Item (Join-Path $modBin 'ChzzkOfTheLamb.Protocol.dll') $pluginOut -Force

Write-Host '[4/5] Publishing self-contained Companion...'
dotnet publish (Join-Path $root 'src\ChzzkOfTheLamb.Companion\ChzzkOfTheLamb.Companion.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $companionOut

Copy-Item (Join-Path $root 'Install-ChzzkOfTheLamb.ps1') $dist -Force
Copy-Item (Join-Path $root 'RELEASE-README.md') $dist -Force

Write-Host '[5/5] Creating ZIP...'
$zip = Join-Path (Split-Path $dist -Parent) 'ChzzkOfTheLamb-v1.0.0-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Release created: $zip"
