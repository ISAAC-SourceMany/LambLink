param(
  [Parameter(Mandatory=$true)][string]$Bucket,
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [string]$DistributionId = ""
)
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot '..\frontend\index.html'
$temp = Join-Path $env:TEMP 'cotl-my-lamb-index.html'

# Windows PowerShell 5.1 Get-Content without an explicit encoding interprets UTF-8
# source files using the active ANSI code page. That corrupts Korean text before
# upload even though S3 serves the file as UTF-8. Read and write explicitly as
# UTF-8 (without BOM) so the bytes uploaded to S3 remain valid UTF-8.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$html = [System.IO.File]::ReadAllText($source, [System.Text.Encoding]::UTF8)
$html = $html.Replace('__API_BASE__', $ApiBaseUrl.TrimEnd('/'))
[System.IO.File]::WriteAllText($temp, $html, $utf8NoBom)
aws s3 cp $temp "s3://$Bucket/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache, no-store, must-revalidate"
if ($LASTEXITCODE -ne 0) { throw "S3 upload failed." }
Write-Host "Frontend uploaded to s3://$Bucket/index.html"
if (-not [string]::IsNullOrWhiteSpace($DistributionId)) {
  aws cloudfront create-invalidation --distribution-id $DistributionId --paths "/index.html" "/"
  if ($LASTEXITCODE -ne 0) { throw "CloudFront invalidation failed." }
  Write-Host "CloudFront invalidation requested: $DistributionId"
}
