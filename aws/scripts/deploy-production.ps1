param(
  [string]$StackName = 'cotl-prod',
  [string]$Profile = '',
  [string]$Region = 'ap-northeast-2'
)
$ErrorActionPreference = 'Stop'
$env:SAM_CLI_TELEMETRY = '0'

$awsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$template = Join-Path $awsRoot 'template.yaml'
$buildDir = Join-Path $awsRoot '.aws-sam\production'
$outputsFile = Join-Path $awsRoot 'production-outputs.local.json'

if (-not (Get-Command aws -ErrorAction SilentlyContinue)) { throw 'AWS CLI is not installed.' }
$samCommand = Get-Command sam -ErrorAction SilentlyContinue
if ($samCommand) { $samCommand = $samCommand.Source }
elseif (Test-Path -LiteralPath 'C:\Program Files\Amazon\AWSSAMCLI\bin\sam.cmd') {
  $samCommand = 'C:\Program Files\Amazon\AWSSAMCLI\bin\sam.cmd'
}
else {
  throw 'AWS SAM CLI is not installed. Install it, reopen PowerShell, and run this script again.'
}

$awsCommon = @('--region', $Region)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
& aws @awsCommon sts get-caller-identity --output json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'AWS authentication failed.' }

$stackStatus = & aws @awsCommon cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].StackStatus' --output text
if ($LASTEXITCODE -ne 0 -or $stackStatus -notmatch '^(CREATE|UPDATE)_COMPLETE$') {
  throw "Production stack is not ready for an update: $StackName ($stackStatus)"
}

Write-Host '[1/5] Building production SAM application...'
& $samCommand build --template-file $template --build-dir $buildDir
if ($LASTEXITCODE -ne 0) { throw 'sam build failed.' }

Write-Host "[2/5] Deploying production stack in place: $StackName"
$deployArgs = @(
  'deploy',
  '--template-file', (Join-Path $buildDir 'template.yaml'),
  '--stack-name', $StackName,
  '--region', $Region,
  '--resolve-s3',
  '--capabilities', 'CAPABILITY_IAM',
  '--parameter-overrides', 'EnvironmentName=prod',
  '--tags', 'Environment=prod', 'Application=LambLink',
  '--no-confirm-changeset',
  '--no-fail-on-empty-changeset'
)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $deployArgs += @('--profile', $Profile) }
& $samCommand @deployArgs
if ($LASTEXITCODE -ne 0) { throw 'sam deploy failed.' }

Write-Host '[3/5] Reading non-sensitive production outputs...'
$outputsJson = & aws @awsCommon cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Outputs' --output json
if ($LASTEXITCODE -ne 0) { throw 'Failed to read production stack outputs.' }
$outputs = @{}
foreach ($item in ($outputsJson | ConvertFrom-Json)) { $outputs[$item.OutputKey] = $item.OutputValue }
foreach ($required in @('EnvironmentName','ApiUrl','FrontendBucketName','FrontendUrl','FrontendDistributionId','CompanionOAuthCallbackUrl','ViewerOAuthCallbackUrl')) {
  if ([string]::IsNullOrWhiteSpace([string]$outputs[$required])) { throw "Missing stack output: $required" }
}
if ([string]$outputs['EnvironmentName'] -ne 'prod') { throw "Unexpected deployed environment: $($outputs['EnvironmentName'])" }
[IO.File]::WriteAllText($outputsFile, ($outputs | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

Write-Host '[4/5] Uploading versioned production frontend and preview assets...'
$frontendArgs = @{
  Bucket = $outputs['FrontendBucketName']
  ApiBaseUrl = $outputs['ApiUrl']
  DistributionId = $outputs['FrontendDistributionId']
  Region = $Region
}
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $frontendArgs['Profile'] = $Profile }
& (Join-Path $PSScriptRoot 'deploy-frontend.ps1') @frontendArgs

Write-Host '[5/5] Checking production health endpoint...'
$health = Invoke-RestMethod -Uri ($outputs['ApiUrl'].TrimEnd('/') + '/health') -Method Get -TimeoutSec 30
if ($health.ok -ne $true) { throw 'Production API health check did not return ok=true.' }

Write-Host '[OK] Production deployment is healthy.'
Write-Host "Frontend: $($outputs['FrontendUrl'])"
Write-Host "Companion callback: $($outputs['CompanionOAuthCallbackUrl'])"
Write-Host "Viewer callback: $($outputs['ViewerOAuthCallbackUrl'])"
Write-Host "Outputs saved locally: $outputsFile"
