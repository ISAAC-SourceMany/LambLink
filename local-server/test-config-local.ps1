$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:COTL_CONFIG_PROVIDER = 'local'

$required = @('CHZZK_CLIENT_ID','CHZZK_CLIENT_SECRET','MYLAMB_CHZZK_CLIENT_ID','MYLAMB_CHZZK_CLIENT_SECRET')
$missing = @($required | Where-Object { -not (Get-Item "Env:$_" -ErrorAction SilentlyContinue).Value })
if ($missing.Count -gt 0) {
    Write-Host "Missing environment variables: $($missing -join ', ')" -ForegroundColor Yellow
    exit 1
}

python "$here\config_smoke_test.py"
