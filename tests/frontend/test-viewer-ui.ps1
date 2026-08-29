$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$frontendPath = Join-Path $projectRoot 'aws\frontend\index.html'
$metadataPath = Join-Path $projectRoot 'Follower.preview.json'
$atlasGenerator = Join-Path $projectRoot 'tools\Generate-FollowerPreviewAtlas.ps1'

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw "Frontend assertion failed: $Message" }
}

$html = [IO.File]::ReadAllText($frontendPath, [Text.Encoding]::UTF8)
& node -e "const fs=require('fs');const h=fs.readFileSync(process.argv[1],'utf8');const m=h.match(/<script>([\s\S]*?)<\/script>/);if(!m)throw new Error('script missing');new Function(m[1]);" $frontendPath
if ($LASTEXITCODE -ne 0) { throw 'Frontend JavaScript syntax validation failed.' }

Assert-True ($html.Contains('id="historyBtn"')) 'history button must always exist'
Assert-True ($html.Contains('아직 생성된 신도 히스토리가 없습니다.')) 'empty history state must be visible'
Assert-True ($html.Contains('Follower.preview.png')) 'browser must use the reduced preview atlas'
Assert-True (-not $html.Contains('${PREVIEW_ASSET_BASE}/Follower.png')) 'browser must not download the 8192px source atlas'

$selectColor = [regex]::Match($html, 'function selectColor\([^\r\n]+').Value
$selectVariant = [regex]::Match($html, 'function selectVariant\([^\r\n]+').Value
Assert-True (-not $selectColor.Contains('renderAll')) 'color selection must not rebuild every picker'
Assert-True (-not $selectVariant.Contains('renderAll')) 'variant selection must not rebuild every picker'
Assert-True ($html.Contains('IntersectionObserver')) 'thumbnail rendering must be viewport-lazy'
Assert-True ($html.Contains("const id=String(variantId??'0');return variants[id]||null")) 'missing variant previews must not silently render variant 1'

$metadata = [IO.File]::ReadAllText($metadataPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$formIds = @($metadata.previews.PSObject.Properties.Name)
foreach ($required in @('Bison', 'Butterfly', 'Eagle', 'Rhino', 'Webber')) {
  Assert-True ($required -in $formIds) "preview metadata must contain $required"
}

$expectedVariantCounts = [ordered]@{
  ChosenChild = 1
  Abomination = 2
  MassiveMonster = 2
  Poop = 2
  Cat = 3
  Ibex = 4
  Lamb = 5
}
foreach ($entry in $expectedVariantCounts.GetEnumerator()) {
  $formPreview = $metadata.previews.PSObject.Properties[$entry.Key]
  Assert-True ($null -ne $formPreview) "preview metadata must contain $($entry.Key)"
  $actualCount = @($formPreview.Value.PSObject.Properties).Count
  Assert-True ($actualCount -eq $entry.Value) "preview metadata must contain $($entry.Value) variants for $($entry.Key), actual=$actualCount"
}

$previewAtlasPath = Join-Path ([IO.Path]::GetTempPath()) ("cotl-preview-test-" + [guid]::NewGuid().ToString('N') + '.png')
try {
  & $atlasGenerator -OutputPath $previewAtlasPath -Size 2048
  Add-Type -AssemblyName System.Drawing
  $image = [Drawing.Image]::FromFile($previewAtlasPath)
  try {
    Assert-True ($image.Width -eq 2048 -and $image.Height -eq 2048) 'generated browser atlas dimensions'
  }
  finally {
    $image.Dispose()
  }
  Assert-True ((Get-Item $previewAtlasPath).Length -lt (Get-Item (Join-Path $projectRoot 'Follower.png')).Length) 'browser atlas must be smaller than source'
}
finally {
  if (Test-Path -LiteralPath $previewAtlasPath) { Remove-Item -LiteralPath $previewAtlasPath -Force }
}

Write-Host "[OK] Viewer UI, history, preview coverage, and reduced-atlas assertions passed. forms=$($formIds.Count)"
