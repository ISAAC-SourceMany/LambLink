param(
  [string]$StackName = 'cotl-chzzk-staging',
  [string]$Profile = '',
  [string]$Region = 'ap-northeast-2'
)
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$release = '1.0.0'
$distributionZip = Join-Path $projectRoot "dist\LambLink-v$release-distribution.zip"
$sourceDir = Join-Path $projectRoot "dist\LambLink-v$release-distribution\CDN-UPLOAD"
$outputFile = Join-Path $projectRoot 'aws\staging-release.local.json'
if (-not (Test-Path -LiteralPath $distributionZip)) { throw "Missing distribution ZIP: $distributionZip" }
if (-not (Test-Path -LiteralPath $sourceDir)) { throw "Missing CDN-UPLOAD directory: $sourceDir" }

$awsCommon = @('--region', $Region)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
& aws @awsCommon sts get-caller-identity --output json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'AWS authentication failed.' }

$outputsJson = & aws @awsCommon cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Outputs' --output json
if ($LASTEXITCODE -ne 0) { throw 'Failed to read staging stack outputs.' }
$outputs = @{}
foreach ($item in ($outputsJson | ConvertFrom-Json)) { $outputs[$item.OutputKey] = $item.OutputValue }
$bucket = [string]$outputs['FrontendBucketName']
$apiUrl = ([string]$outputs['ApiUrl']).TrimEnd('/')
$frontendUrl = ([string]$outputs['FrontendUrl']).TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($bucket) -or [string]::IsNullOrWhiteSpace($apiUrl) -or [string]::IsNullOrWhiteSpace($frontendUrl)) {
  throw 'Staging stack is missing frontend outputs.'
}

$buildHash = (Get-FileHash -LiteralPath $distributionZip -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
$prefix = "releases-staging/$release-$buildHash"
$manifestName = "installer-manifest-$release.json"
$sourceManifest = Join-Path $sourceDir $manifestName
$tempManifest = Join-Path ([IO.Path]::GetTempPath()) ("cotl-staging-manifest-" + [guid]::NewGuid().ToString('N') + '.json')
$componentFiles = @{
  'korean-font-fix' = 'COTL-KoreanFontFix-4.2.1-v1.0.0.zip'
  'lamblink-mod' = "LambLink-Mod-$release.zip"
  'companion' = "LambLink-Companion-$release-win-x64.zip"
}

try {
  $manifest = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
  if ($manifest.release -ne $release) { throw "Unexpected manifest release: $($manifest.release)" }
  $manifest | Add-Member -NotePropertyName environment -NotePropertyValue 'staging' -Force
  $manifest | Add-Member -NotePropertyName apiBaseUrl -NotePropertyValue $apiUrl -Force
  $manifest | Add-Member -NotePropertyName frontendUrl -NotePropertyValue $frontendUrl -Force
  foreach ($component in $manifest.components) {
    if ($componentFiles.ContainsKey([string]$component.id)) {
      $fileName = $componentFiles[[string]$component.id]
      $component.url = "$frontendUrl/$prefix/$fileName"
      $actualHash = (Get-FileHash -LiteralPath (Join-Path $sourceDir $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
      if ($actualHash -ne ([string]$component.sha256).ToLowerInvariant()) {
        throw "Source manifest hash mismatch: $($component.id)"
      }
    }
  }
  [IO.File]::WriteAllText($tempManifest, ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))

  foreach ($fileName in $componentFiles.Values) {
    & aws @awsCommon s3 cp (Join-Path $sourceDir $fileName) "s3://$bucket/$prefix/$fileName" --content-type 'application/zip' --cache-control 'public,max-age=31536000,immutable'
    if ($LASTEXITCODE -ne 0) { throw "Staging release upload failed: $fileName" }
  }
  & aws @awsCommon s3 cp $tempManifest "s3://$bucket/$prefix/$manifestName" --content-type 'application/json; charset=utf-8' --cache-control 'no-cache, no-store, must-revalidate'
  if ($LASTEXITCODE -ne 0) { throw 'Staging installer manifest upload failed.' }

  $result = @{
    Release = $release
    BuildHash = $buildHash
    ManifestUrl = "$frontendUrl/$prefix/$manifestName"
    InstallerPath = (Join-Path $projectRoot "dist\LambLink-v$release-distribution\USER-DOWNLOAD\LambLink-Setup-$release.exe")
  }
  [IO.File]::WriteAllText($outputFile, ($result | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
  Write-Host "[OK] Staging installer manifest: $($result.ManifestUrl)"
  Write-Host "[TEST] `$env:COTL_INSTALLER_MANIFEST_URL='$($result.ManifestUrl)'"
  Write-Host "[NEXT] powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\aws\scripts\install-release-staging.ps1"
}
finally {
  if (Test-Path -LiteralPath $tempManifest) { Remove-Item -LiteralPath $tempManifest -Force }
}
