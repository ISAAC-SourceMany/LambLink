param(
  [string]$StackName = 'cotl-prod',
  [string]$Profile = '',
  [string]$Region = 'ap-northeast-2'
)
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$release = '1.0.4'
$distributionZip = Join-Path $projectRoot "dist\LambLink-v$release-distribution.zip"
$sourceDir = Join-Path $projectRoot "dist\LambLink-v$release-distribution\CDN-UPLOAD"
$outputFile = Join-Path $projectRoot 'aws\production-release.local.json'
if (-not (Test-Path -LiteralPath $distributionZip)) { throw "Missing distribution ZIP: $distributionZip" }
if (-not (Test-Path -LiteralPath $sourceDir)) { throw "Missing CDN-UPLOAD directory: $sourceDir" }

$awsCommon = @('--region', $Region)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
& aws @awsCommon sts get-caller-identity --output json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'AWS authentication failed.' }

$outputsJson = & aws @awsCommon cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Outputs' --output json
if ($LASTEXITCODE -ne 0) { throw 'Failed to read production stack outputs.' }
$outputs = @{}
foreach ($item in ($outputsJson | ConvertFrom-Json)) { $outputs[$item.OutputKey] = $item.OutputValue }
$bucket = [string]$outputs['FrontendBucketName']
$frontendUrl = ([string]$outputs['FrontendUrl']).TrimEnd('/')
$distributionId = [string]$outputs['FrontendDistributionId']
if ([string]$outputs['EnvironmentName'] -ne 'prod') { throw "Expected EnvironmentName=prod, actual=$($outputs['EnvironmentName'])" }
if ([string]::IsNullOrWhiteSpace($bucket) -or [string]::IsNullOrWhiteSpace($frontendUrl) -or [string]::IsNullOrWhiteSpace($distributionId)) {
  throw 'Production stack is missing frontend outputs.'
}

$manifestName = "installer-manifest-$release.json"
$manifestPath = Join-Path $sourceDir $manifestName
$componentFiles = @(
  "LambLink-Mod-$release.zip",
  "LambLink-Companion-$release-win-x64.zip"
)
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.release -ne $release -or $manifest.environment -ne 'production') {
  throw "Unexpected production manifest identity: release=$($manifest.release), environment=$($manifest.environment)"
}
if (([string]$manifest.frontendUrl).TrimEnd('/') -ne $frontendUrl) {
  throw "Manifest frontend does not match production stack: $($manifest.frontendUrl)"
}

foreach ($fileName in $componentFiles) {
  $path = Join-Path $sourceDir $fileName
  if (-not (Test-Path -LiteralPath $path)) { throw "Missing production component: $path" }
  & aws @awsCommon s3 cp $path "s3://$bucket/releases/$fileName" --content-type 'application/zip' --cache-control 'public,max-age=31536000,immutable'
  if ($LASTEXITCODE -ne 0) { throw "Production release upload failed: $fileName" }
}
& aws @awsCommon s3 cp $manifestPath "s3://$bucket/releases/$manifestName" --content-type 'application/json; charset=utf-8' --cache-control 'no-cache, no-store, must-revalidate'
if ($LASTEXITCODE -ne 0) { throw 'Production installer manifest upload failed.' }

& aws @awsCommon cloudfront create-invalidation --distribution-id $distributionId --paths "/releases/$manifestName" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Production release manifest invalidation failed.' }

$result = [ordered]@{
  Release = $release
  ManifestUrl = "$frontendUrl/releases/$manifestName"
  InstallerPath = (Join-Path $projectRoot "dist\LambLink-v$release-distribution\USER-DOWNLOAD\LambLink-Setup-$release.exe")
  DistributionSha256 = (Get-FileHash -LiteralPath $distributionZip -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText($outputFile, ($result | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host "[OK] Production installer manifest: $($result.ManifestUrl)"
Write-Host "[OK] Production installer: $($result.InstallerPath)"
