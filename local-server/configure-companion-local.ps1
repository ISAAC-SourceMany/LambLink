$env:COTL_WEB_API_BASE = 'http://127.0.0.1:17882'
$env:COTL_WEB_FRONTEND_URL = 'http://127.0.0.1:17882/'
Write-Host "Configured this PowerShell process for the local My Lamb server."
Write-Host "COTL_WEB_API_BASE=$env:COTL_WEB_API_BASE"
Write-Host "COTL_WEB_FRONTEND_URL=$env:COTL_WEB_FRONTEND_URL"
Write-Host "NOTE: this script configures only the My Lamb endpoint."
Write-Host "For AWS SSO-backed CHZZK credentials too, use configure-companion-local-aws.ps1."
Write-Host "Run the Companion EXE from THIS SAME PowerShell window."
