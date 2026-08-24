$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '[1/2] Building RC26 game Mod...'
& (Join-Path $root 'build-plugin.ps1')
if (-not $?) { throw 'RC26 game Mod build script failed.' }

Write-Host '[2/2] Building RC26 Companion...'
& (Join-Path $root 'build-companion.ps1')
if (-not $?) { throw 'RC26 Companion build script failed.' }

$mod = Join-Path $root 'dist\rc26-plugin\ChzzkOfTheLamb.Mod.dll'
$protocol = Join-Path $root 'dist\rc26-plugin\ChzzkOfTheLamb.Protocol.dll'
$companion = Join-Path $root 'dist\rc26-companion\ChzzkOfTheLamb.Companion.exe'
foreach ($path in @($mod, $protocol, $companion)) {
  if (-not (Test-Path $path)) { throw "RC26 matched-pair output is missing: $path" }
}

$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companion)
if ($companionVersion.FileVersion -ne '1.0.0.26') {
  throw "Unexpected Companion file version: $($companionVersion.FileVersion)"
}

Write-Host '[OK] RC26 matched Mod + Companion test pair is ready.'
Write-Host "Mod:       $mod"
Write-Host "Protocol:  $protocol"
Write-Host "Companion: $companion"
