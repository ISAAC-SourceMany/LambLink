$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:COTL_CONFIG_PROVIDER = 'aws'

if (-not $env:AWS_DEFAULT_REGION -and -not $env:AWS_REGION) {
    $env:AWS_DEFAULT_REGION = 'ap-northeast-2'
}

# Windows PowerShell 5.1 does not support the ?? operator.
$region = $env:AWS_REGION
if (-not $region) { $region = $env:AWS_DEFAULT_REGION }

# Prefer the project SSO profile if the caller did not explicitly choose one.
if (-not $env:AWS_PROFILE) {
    try {
        $profiles = & aws configure list-profiles 2>$null
        if ($LASTEXITCODE -eq 0 -and ($profiles -contains 'cotl-dev')) {
            $env:AWS_PROFILE = 'cotl-dev'
        }
    } catch {
        # Credential errors will be reported by the Python smoke test below.
    }
}

Write-Host "Testing production-style configuration retrieval from AWS..."
Write-Host "Provider: AWS SSM Parameter Store + Secrets Manager"
Write-Host "Region: $region"
if ($env:AWS_PROFILE) {
    Write-Host "Profile: $env:AWS_PROFILE"
} else {
    Write-Host "Profile: (AWS SDK default credential chain)" -ForegroundColor Yellow
}

try {
    py -c "import boto3" 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'boto3 missing' }
} catch {
    Write-Host "boto3 is not installed. Run: py -m pip install boto3" -ForegroundColor Yellow
    exit 1
}

& py "$here\config_smoke_test.py"
exit $LASTEXITCODE
