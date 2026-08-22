$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $env:COTL_CONFIG_PROVIDER) {
    $env:COTL_CONFIG_PROVIDER = 'local'
}

if ($env:COTL_CONFIG_PROVIDER -eq 'local') {
    $required = @('CHZZK_CLIENT_ID','CHZZK_CLIENT_SECRET','MYLAMB_CHZZK_CLIENT_ID','MYLAMB_CHZZK_CLIENT_SECRET')
    $missing = @($required | Where-Object { -not (Get-Item "Env:$_" -ErrorAction SilentlyContinue).Value })
    if ($missing.Count -gt 0) {
        Write-Host "Local provider requires: $($missing -join ', ')" -ForegroundColor Yellow
        exit 1
    }
} elseif ($env:COTL_CONFIG_PROVIDER -eq 'aws') {
    if (-not $env:AWS_DEFAULT_REGION -and -not $env:AWS_REGION) { $env:AWS_DEFAULT_REGION = 'ap-northeast-2' }
    Write-Host "Using AWS configuration provider (SSM + Secrets Manager) from this PC."
} else {
    Write-Host "Unsupported COTL_CONFIG_PROVIDER: $env:COTL_CONFIG_PROVIDER" -ForegroundColor Yellow
    exit 1
}

$env:COTL_LOCAL_CHZZK_REDIRECT_URI = 'http://127.0.0.1:17882/auth/chzzk/callback'
Write-Host "Starting My Lamb viewer OAuth server..."
Write-Host "Config provider: $env:COTL_CONFIG_PROVIDER"
Write-Host "Redirect URI: $env:COTL_LOCAL_CHZZK_REDIRECT_URI"
python "$here\server.py"
