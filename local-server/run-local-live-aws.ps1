$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:COTL_CONFIG_PROVIDER = 'aws'
if (-not $env:AWS_DEFAULT_REGION -and -not $env:AWS_REGION) { $env:AWS_DEFAULT_REGION = 'ap-northeast-2' }
& "$here\run-local-live.ps1"
