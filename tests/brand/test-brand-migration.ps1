$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
}

$requiredPaths = @(
  'LambLink.sln',
  'src\LambLink.Mod\LambLink.Mod.csproj',
  'src\LambLink.Protocol\LambLink.Protocol.csproj',
  'src\LambLink.Companion\LambLink.Companion.csproj',
  'src\LambLink.Installer\LambLink.Installer.csproj'
)
foreach ($relativePath in $requiredPaths) {
  Assert-True (Test-Path -LiteralPath (Join-Path $root $relativePath) -PathType Leaf) "Missing LambLink project file: $relativePath"
}

$oldBrandPattern = 'ChzzkOfTheLamb'
$allowedLegacyCounts = @{
  'src\LambLink.Companion\Diagnostics\SupportBundleService.cs' = 5
  'src\LambLink.Companion\Program.cs' = 2
  'src\LambLink.Installer\Program.cs' = 8
  'src\LambLink.Mod\Plugin.cs' = 2
}
$scanRoots = @(
  (Join-Path $root 'src'),
  (Join-Path $root 'aws\backend'),
  (Join-Path $root 'aws\frontend'),
  (Join-Path $root 'aws\scripts'),
  (Join-Path $root 'installer')
)
$textExtensions = @('.cs','.csproj','.py','.html','.js','.ps1','.json','.yaml','.yml')
$foundLegacyFiles = @{}
foreach ($scanRoot in $scanRoots) {
  foreach ($file in Get-ChildItem -LiteralPath $scanRoot -Recurse -File) {
    if ($file.FullName -match '[\\/]bin[\\/]|[\\/]obj[\\/]|[\\/]\.aws-sam[\\/]' -or $file.Extension -notin $textExtensions) { continue }
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $count = [regex]::Matches($content, $oldBrandPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
    if ($count -eq 0) { continue }
    $relativePath = $file.FullName.Substring($root.TrimEnd('\').Length).TrimStart('\')
    $foundLegacyFiles[$relativePath] = $count
  }
}

foreach ($entry in $foundLegacyFiles.GetEnumerator()) {
  Assert-True ($allowedLegacyCounts.ContainsKey($entry.Key)) "Unexpected legacy brand reference: $($entry.Key)"
  Assert-True ($allowedLegacyCounts[$entry.Key] -eq $entry.Value) "Unexpected legacy brand reference count in $($entry.Key): expected=$($allowedLegacyCounts[$entry.Key]), actual=$($entry.Value)"
}
foreach ($entry in $allowedLegacyCounts.GetEnumerator()) {
  Assert-True ($foundLegacyFiles.ContainsKey($entry.Key)) "Required compatibility reference was removed: $($entry.Key)"
}

$pluginSource = Get-Content -LiteralPath (Join-Path $root 'src\LambLink.Mod\Plugin.cs') -Raw
Assert-True ($pluginSource.Contains('PluginGuid = "com.chzzkofthelamb.integration"')) 'The legacy BepInEx GUID must remain stable for in-place upgrades.'
Assert-True ($pluginSource.Contains('PluginName = "LambLink"')) 'The user-visible BepInEx plugin name must be LambLink.'

$installerSource = Get-Content -LiteralPath (Join-Path $root 'src\LambLink.Installer\Program.cs') -Raw
$copyIndex = $installerSource.IndexOf('CopyDirectory(extracted, gameRoot);', [StringComparison]::Ordinal)
$verifyIndex = $installerSource.IndexOf('VerifyCopiedDirectory(extracted, gameRoot);', [StringComparison]::Ordinal)
$removeIndex = $installerSource.IndexOf('RemoveLegacyModDirectory(gameRoot);', [StringComparison]::Ordinal)
Assert-True ($copyIndex -ge 0 -and $verifyIndex -gt $copyIndex -and $removeIndex -gt $verifyIndex) 'Legacy Mod removal must happen only after the LambLink Mod copy is verified.'

$stagingInstallerSource = Get-Content -LiteralPath (Join-Path $root 'aws\scripts\install-release-staging.ps1') -Raw
Assert-True ($stagingInstallerSource.Contains('$currentInstallerPath')) 'Staging installer must recover from a repository path move.'

$frontendSource = Get-Content -LiteralPath (Join-Path $root 'aws\frontend\index.html') -Raw
Assert-True ($frontendSource.Contains('<title>LambLink · My Lamb</title>')) 'Viewer page title must include the LambLink brand.'

$templateSource = Get-Content -LiteralPath (Join-Path $root 'aws\template.yaml') -Raw
Assert-True ($templateSource.Contains('Description: LambLink viewer appearance service')) 'AWS application description must use LambLink.'
Assert-True (-not $templateSource.Contains('Description: COTL CHZZK Companion viewer appearance service')) 'Old AWS application description remains.'

Write-Host '[OK] LambLink brand, compatibility allowlist, installer order, and path-move assertions passed.'
