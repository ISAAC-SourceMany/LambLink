$ErrorActionPreference = 'Stop'

if (-not $env:AWS_PROFILE) { $env:AWS_PROFILE = 'cotl-dev' }
if (-not $env:AWS_DEFAULT_REGION) { $env:AWS_DEFAULT_REGION = 'ap-northeast-2' }

Write-Host "Checking AWS SSO profile '$env:AWS_PROFILE'..."
try {
    aws sts get-caller-identity --profile $env:AWS_PROFILE --region $env:AWS_DEFAULT_REGION | Out-Null
} catch {
    Write-Host "AWS SSO session is not usable. Run:" -ForegroundColor Yellow
    Write-Host "  aws sso login --profile $env:AWS_PROFILE" -ForegroundColor Yellow
    throw
}

$env:COTL_COMPANION_CONFIG_PROVIDER = 'aws-cli'
$env:COTL_WEB_API_BASE = 'http://127.0.0.1:17882'
$env:COTL_WEB_FRONTEND_URL = 'http://127.0.0.1:17882/'

Write-Host "Configured this PowerShell process for local My Lamb + AWS-backed Companion credentials."
Write-Host "AWS_PROFILE=$env:AWS_PROFILE"
Write-Host "AWS_DEFAULT_REGION=$env:AWS_DEFAULT_REGION"
Write-Host "COTL_COMPANION_CONFIG_PROVIDER=$env:COTL_COMPANION_CONFIG_PROVIDER"
Write-Host "COTL_WEB_API_BASE=$env:COTL_WEB_API_BASE"
Write-Host "COTL_WEB_FRONTEND_URL=$env:COTL_WEB_FRONTEND_URL"
Write-Host "Run the Companion EXE from THIS SAME PowerShell window."
