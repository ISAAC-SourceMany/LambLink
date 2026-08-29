param(
  [Parameter(Mandatory=$true)][string]$Bucket,
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [string]$DistributionId = "",
  [string]$Profile = "",
  [string]$Region = "",
  [ValidateRange(1024, 4096)][int]$PreviewAtlasSize = 4096
)
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot '..\frontend\index.html'
$temp = Join-Path ([IO.Path]::GetTempPath()) ("cotl-my-lamb-index-" + [guid]::NewGuid().ToString('N') + '.html')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$previewSource = Join-Path $projectRoot 'Follower.png'
$previewMetadata = Join-Path $projectRoot 'Follower.preview.json'
$previewAtlasGenerator = Join-Path $projectRoot 'tools\Generate-FollowerPreviewAtlas.ps1'
$previewAtlasTemp = Join-Path ([IO.Path]::GetTempPath()) ("cotl-follower-preview-" + [guid]::NewGuid().ToString('N') + '.png')
foreach ($requiredPath in @($previewSource, $previewMetadata, $previewAtlasGenerator)) {
  if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Missing follower preview source: $requiredPath" }
}
$previewDocument = [IO.File]::ReadAllText($previewMetadata, [Text.Encoding]::UTF8) | ConvertFrom-Json
$previewFormIds = @($previewDocument.previews.PSObject.Properties.Name)
$requiredRc38PreviewForms = @('Bison', 'Butterfly', 'Eagle', 'Rhino', 'Webber')
$missingPreviewForms = @($requiredRc38PreviewForms | Where-Object { $_ -notin $previewFormIds })
if ([int]$previewDocument.version -ne 1 -or $previewFormIds.Count -lt 37 -or $missingPreviewForms.Count -gt 0) {
  throw "Follower preview metadata is incomplete. forms=$($previewFormIds.Count), missing=$($missingPreviewForms -join ',')"
}
$requiredVariantCounts = [ordered]@{ ChosenChild = 1; Abomination = 2; MassiveMonster = 2; Poop = 2; Ibex = 4; Lamb = 5 }
foreach ($entry in $requiredVariantCounts.GetEnumerator()) {
  $formPreview = $previewDocument.previews.PSObject.Properties[$entry.Key]
  $actualCount = if ($null -eq $formPreview) { 0 } else { @($formPreview.Value.PSObject.Properties).Count }
  if ($actualCount -ne $entry.Value) {
    throw "Follower preview variants are incomplete. form=$($entry.Key), expected=$($entry.Value), actual=$actualCount"
  }
}
& $previewAtlasGenerator -SourcePath $previewSource -OutputPath $previewAtlasTemp -Size $PreviewAtlasSize
$assets = [ordered]@{
  'Follower.preview.json' = $previewMetadata
  'Follower.preview.png' = $previewAtlasTemp
}
$assetPaths = @($assets.Values)
$assetFingerprint = (($assetPaths | ForEach-Object {
  (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
}) -join '')
$assetBase = "assets/v1.0.0-$assetFingerprint"
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
  $contentTypes = @{
    'Follower.preview.json' = 'application/json; charset=utf-8'
    'Follower.preview.png' = 'image/png'
  }
  foreach ($asset in $assets.Keys) {
    & aws @awsCommon s3 cp $assets[$asset] "s3://$Bucket/$assetBase/$asset" --content-type $contentTypes[$asset] --cache-control "public,max-age=31536000,immutable"
    if ($LASTEXITCODE -ne 0) { throw "Follower preview asset upload failed: $asset" }
  }
  # Publish the HTML only after every immutable asset exists so a viewer can
  # never observe an index that references objects which are still uploading.
  & aws @awsCommon s3 cp $temp "s3://$Bucket/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache, no-store, must-revalidate"
  if ($LASTEXITCODE -ne 0) { throw "S3 upload failed." }
  Write-Host "Frontend uploaded to s3://$Bucket/index.html (assets=$assetBase)"
  if (-not [string]::IsNullOrWhiteSpace($DistributionId)) {
    & aws @awsCommon cloudfront create-invalidation --distribution-id $DistributionId --paths "/index.html" "/"
    if ($LASTEXITCODE -ne 0) { throw "CloudFront invalidation failed." }
    Write-Host "CloudFront invalidation requested: $DistributionId"
  }
}
finally {
  if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
  if (Test-Path -LiteralPath $previewAtlasTemp) { Remove-Item -LiteralPath $previewAtlasTemp -Force }
}
