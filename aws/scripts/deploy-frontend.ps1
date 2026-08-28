param(
  [Parameter(Mandatory=$true)][string]$Bucket,
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [string]$DistributionId = "",
  [string]$Profile = "",
  [string]$Region = ""
)
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot '..\frontend\index.html'
$temp = Join-Path ([IO.Path]::GetTempPath()) ("cotl-my-lamb-index-" + [guid]::NewGuid().ToString('N') + '.html')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$assetNames = @('Follower.atlas.bytes', 'Follower.skel.bytes', 'Follower.png')
$assetPaths = @($assetNames | ForEach-Object { Join-Path $projectRoot $_ })
foreach ($assetPath in $assetPaths) {
  if (-not (Test-Path -LiteralPath $assetPath)) { throw "Missing follower preview asset: $assetPath" }
}
$assetFingerprint = (($assetPaths | ForEach-Object {
  (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
}) -join '')
$assetBase = "assets/rc35-$assetFingerprint"
$awsCommon = @()
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
if (-not [string]::IsNullOrWhiteSpace($Region)) { $awsCommon += @('--region', $Region) }

# Windows PowerShell 5.1 Get-Content without an explicit encoding interprets UTF-8
# source files using the active ANSI code page. That corrupts Korean text before
# upload even though S3 serves the file as UTF-8. Read and write explicitly as
# UTF-8 (without BOM) so the bytes uploaded to S3 remain valid UTF-8.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$html = [System.IO.File]::ReadAllText($source, [System.Text.Encoding]::UTF8)
$html = $html.Replace('__API_BASE__', $ApiBaseUrl.TrimEnd('/'))
$html = $html.Replace('__PREVIEW_ASSET_BASE__', $assetBase)
try {
  [System.IO.File]::WriteAllText($temp, $html, $utf8NoBom)
  & aws @awsCommon s3 cp $temp "s3://$Bucket/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache, no-store, must-revalidate"
  if ($LASTEXITCODE -ne 0) { throw "S3 upload failed." }

  $contentTypes = @{
    'Follower.atlas.bytes' = 'application/octet-stream'
    'Follower.skel.bytes' = 'application/octet-stream'
    'Follower.png' = 'image/png'
  }
  foreach ($asset in $assetNames) {
    & aws @awsCommon s3 cp (Join-Path $projectRoot $asset) "s3://$Bucket/$assetBase/$asset" --content-type $contentTypes[$asset] --cache-control "public,max-age=31536000,immutable"
    if ($LASTEXITCODE -ne 0) { throw "Follower preview asset upload failed: $asset" }
  }
  Write-Host "Frontend uploaded to s3://$Bucket/index.html (assets=$assetBase)"
  if (-not [string]::IsNullOrWhiteSpace($DistributionId)) {
    & aws @awsCommon cloudfront create-invalidation --distribution-id $DistributionId --paths "/index.html" "/"
    if ($LASTEXITCODE -ne 0) { throw "CloudFront invalidation failed." }
    Write-Host "CloudFront invalidation requested: $DistributionId"
  }
}
finally {
  if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
