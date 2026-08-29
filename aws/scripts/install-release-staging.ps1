param(
  [string]$DeploymentInfoPath = '',
  [switch]$AllowOtherCompanionProcess
)
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($DeploymentInfoPath)) {
  $DeploymentInfoPath = Join-Path $projectRoot 'aws\staging-release.local.json'
}
$resolvedInfoPath = [IO.Path]::GetFullPath($DeploymentInfoPath)
if (-not (Test-Path -LiteralPath $resolvedInfoPath -PathType Leaf)) {
  throw "Staging deployment information not found: $resolvedInfoPath"
}

$deployment = Get-Content -LiteralPath $resolvedInfoPath -Raw | ConvertFrom-Json
$manifestUrl = [string]$deployment.ManifestUrl
$installerPath = [IO.Path]::GetFullPath([string]$deployment.InstallerPath)
if ([string]$deployment.Release -ne '1.0.0-rc39') {
  throw "Expected RC39 staging deployment information, actual=$($deployment.Release)"
}
$manifestUsesHttps = $manifestUrl.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)
$manifestUsesStagingPath = $manifestUrl.IndexOf('/releases-staging/', [StringComparison]::Ordinal) -ge 0
if (-not $manifestUsesHttps -or -not $manifestUsesStagingPath) {
  throw "Refusing to launch the installer with a non-staging manifest URL: $manifestUrl"
}
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
  throw "RC39 installer not found: $installerPath"
}

$runningCompanions = @(Get-Process -Name 'LambLink.Companion' -ErrorAction SilentlyContinue)
if ($runningCompanions.Count -gt 0 -and -not $AllowOtherCompanionProcess) {
  foreach ($process in $runningCompanions) { $process.Dispose() }
  throw 'Companion이 실행 중입니다. 운영 Companion을 종료한 뒤 다시 실행하세요. 운영과 스테이징 Companion은 동시에 실행하지 않습니다.'
}
foreach ($process in $runningCompanions) { $process.Dispose() }

$previousManifestUrl = [Environment]::GetEnvironmentVariable('COTL_INSTALLER_MANIFEST_URL', 'Process')
try {
  $env:COTL_INSTALLER_MANIFEST_URL = $manifestUrl
  Write-Host '============================================================'
  Write-Host '[STAGING TEST] RC39 스테이징 설치기를 실행합니다.' -ForegroundColor Yellow
  Write-Host "Manifest: $manifestUrl"
  Write-Host "Installer: $installerPath"
  Write-Host '============================================================'
  $installerProcess = Start-Process -FilePath $installerPath -PassThru -Wait
  if ($installerProcess.ExitCode -ne 0) {
    throw "Staging installer exited with code $($installerProcess.ExitCode)."
  }
  Write-Host '[NEXT] 바탕 화면의 LambLink Companion (STAGING TEST) 바로가기를 실행하세요.'
  Write-Host '[EXPECT] 프로그램: %LOCALAPPDATA%\Programs\LambLink-Staging'
  Write-Host '[EXPECT] 데이터/로그: %LOCALAPPDATA%\LambLink-Staging'
}
finally {
  [Environment]::SetEnvironmentVariable('COTL_INSTALLER_MANIFEST_URL', $previousManifestUrl, 'Process')
}
