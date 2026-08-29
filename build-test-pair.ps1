$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '[1/2] Building v1.0.0 game Mod...'
& (Join-Path $root 'build-plugin.ps1')
if (-not $?) { throw 'v1.0.0 game Mod build script failed.' }

Write-Host '[2/2] Building v1.0.0 Companion...'
& (Join-Path $root 'build-companion.ps1')
if (-not $?) { throw 'v1.0.0 Companion build script failed.' }

$mod = Join-Path $root 'dist\v1.0.0-plugin-test\LambLink.Mod.dll'
$protocol = Join-Path $root 'dist\v1.0.0-plugin-test\LambLink.Protocol.dll'
$companion = Join-Path $root 'dist\v1.0.0-companion-test\LambLink.Companion.exe'
foreach ($path in @($mod, $protocol, $companion)) {
  if (-not (Test-Path $path)) { throw "v1.0.0 matched-pair output is missing: $path" }
}

$companionVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($companion)
if ($companionVersion.FileVersion -ne '1.0.0.40') {
  throw "Unexpected Companion file version: $($companionVersion.FileVersion)"
}

Write-Host '[OK] v1.0.0 matched Mod + Companion diagnostic test pair is ready.'
Write-Host '[TEST ONLY] This Companion contains RELEASE_TEST_TOOLS (including dev donation). Do not distribute it.'
Write-Host "Mod:       $mod"
Write-Host "Protocol:  $protocol"
Write-Host "Companion: $companion"
