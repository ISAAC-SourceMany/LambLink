param(
  [string]$StackName = 'cotl-chzzk-staging',
  [string]$Profile = 'cotl-staging',
  [string]$Region = 'ap-northeast-2',
  [string]$CompanionPath = '',
  [string]$DataDirectory = ''
)
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($CompanionPath)) {
  $CompanionPath = Join-Path $projectRoot 'dist\ChzzkOfTheLamb-v1.0.0-rc35\Companion\ChzzkOfTheLamb.Companion.exe'
}
$resolvedCompanion = [IO.Path]::GetFullPath($CompanionPath)
if (-not (Test-Path -LiteralPath $resolvedCompanion -PathType Leaf)) {
  throw "Staging Companion executable not found: $resolvedCompanion"
}

$awsCommon = @('--region', $Region)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
$outputsJson = & aws @awsCommon cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Outputs' --output json
if ($LASTEXITCODE -ne 0) { throw 'Failed to read staging stack outputs.' }
$outputs = @{}
foreach ($item in ($outputsJson | ConvertFrom-Json)) { $outputs[[string]$item.OutputKey] = [string]$item.OutputValue }
$apiUrl = ([string]$outputs['ApiUrl']).TrimEnd('/')
$frontendUrl = ([string]$outputs['FrontendUrl']).TrimEnd('/')
$apiUsesHttps = $apiUrl.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)
$frontendUsesHttps = $frontendUrl.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)
if (-not $apiUsesHttps -or -not $frontendUsesHttps) {
  throw 'Staging stack must expose HTTPS API and frontend URLs.'
}

if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
  $DataDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'ChzzkOfTheLamb-Staging'
}
$resolvedDataDirectory = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($DataDirectory))

$names = @('COTL_STAGING_MODE','COTL_WEB_API_BASE','COTL_WEB_FRONTEND_URL','COTL_STAGING_DATA_DIR')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

try {
  $env:COTL_STAGING_MODE = '1'
  $env:COTL_WEB_API_BASE = $apiUrl
  $env:COTL_WEB_FRONTEND_URL = $frontendUrl
  $env:COTL_STAGING_DATA_DIR = $resolvedDataDirectory
  Write-Host '[STAGING] Companion will use the isolated AWS stack and data directory.'
  Write-Host "[STAGING] API: $apiUrl"
  Write-Host "[STAGING] Viewer page: $frontendUrl"
  Write-Host "[STAGING] Data: $resolvedDataDirectory"
  & $resolvedCompanion
  if ($LASTEXITCODE -ne 0) { throw "Staging Companion exited with code $LASTEXITCODE." }
}
finally {
  foreach ($name in $names) {
    [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
  }
}
