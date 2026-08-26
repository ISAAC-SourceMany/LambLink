$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = '1.0.0-rc35'
$hosting = Join-Path $root 'release-hosting'
$distRoot = Join-Path $root 'dist'
$bundleRoot = Join-Path $distRoot "ChzzkOfTheLamb-v$release-distribution"
$userDir = Join-Path $bundleRoot 'USER-DOWNLOAD'
$cdnDir = Join-Path $bundleRoot 'CDN-UPLOAD'
$bundleZip = Join-Path $distRoot "ChzzkOfTheLamb-v$release-distribution.zip"

function Get-LowerSha256([string]$Path) {
    return (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Remove-DirectoryTree([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.Directory]::Exists($fullPath)) { return }

    try {
        Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction Stop
    }
    catch {
        if (-not [System.IO.Directory]::Exists($fullPath)) {
            Write-Host "[CLEAN] Removed after transient PowerShell path race: $fullPath"
            return
        }

        $extendedPath = if ($fullPath.StartsWith('\\')) {
            '\\?\UNC\' + $fullPath.Substring(2)
        } else {
            '\\?\' + $fullPath
        }

        try {
            [System.IO.Directory]::Delete($extendedPath, $true)
        }
        catch {
            if ([System.IO.Directory]::Exists($fullPath)) {
                throw "Failed to clean build directory: $fullPath ($($_.Exception.Message))"
            }
        }
    }

    if ([System.IO.Directory]::Exists($fullPath)) {
        throw "Build directory still exists after cleanup: $fullPath"
    }
}

Write-Host "[1/5] Building $release runtime pair and GUI installer..."
& (Join-Path $root 'build-release.ps1')

Write-Host '[2/5] Fetching official dependency hashes and creating the pinned manifest...'
& (Join-Path $root 'prepare-installer-manifest.ps1')

$setupName = "ChzzkOfTheLamb-Setup-$release.exe"
$manifestName = "installer-manifest-$release.json"
$fontName = 'COTL-KoreanFontFix-4.2.1-rc35.zip'
$modName = "ChzzkOfTheLamb-Mod-$release.zip"
$companionName = "ChzzkOfTheLamb-Companion-$release-win-x64.zip"
$requiredHostingFiles = @($setupName, $manifestName, $fontName, $modName, $companionName)

Write-Host '[3/5] Validating release identity, manifest URLs, and component SHA-256 values...'
foreach ($name in $requiredHostingFiles) {
    $path = Join-Path $hosting $name
    if (-not (Test-Path $path)) { throw "Missing release output: $path" }
}

$manifestPath = Join-Path $hosting $manifestName
$manifestText = Get-Content $manifestPath -Raw
if ($manifestText -match '__[A-Z0-9_]+__') { throw 'Pinned manifest contains an unresolved placeholder.' }
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.release -ne $release) { throw "Unexpected manifest release: $($manifest.release)" }

$localComponents = @{
    'bepinex' = 'BepInEx_x64_5.4.21.0.zip'
    'cotl-api' = 'xhayper-COTL_API-0.3.4.zip'
    'korean-font-fix' = $fontName
    'chzzk-mod' = $modName
    'companion' = $companionName
}
foreach ($componentId in $localComponents.Keys) {
    $component = @($manifest.components | Where-Object { $_.id -eq $componentId })
    if ($component.Count -ne 1) { throw "Manifest component count is not one: $componentId" }
    $fileName = $localComponents[$componentId]
    if ($componentId -in @('korean-font-fix', 'chzzk-mod', 'companion')) {
        $expectedUrlSuffix = "/releases/$fileName"
        if (-not $component[0].url.EndsWith($expectedUrlSuffix, [System.StringComparison]::Ordinal)) {
            throw "Manifest URL is not pinned to $fileName : $($component[0].url)"
        }
    }
    $actualHash = Get-LowerSha256 (Join-Path $hosting $fileName)
    if ($component[0].sha256.ToLowerInvariant() -ne $actualHash) {
        throw "Manifest SHA-256 mismatch for $componentId"
    }
    Write-Host "[VERIFY] $componentId URL and SHA-256 OK."
}

Write-Host '[4/5] Assembling user-download and CDN-upload folders...'
Remove-DirectoryTree $bundleRoot
if (Test-Path $bundleZip) { Remove-Item $bundleZip -Force }
New-Item -ItemType Directory -Force -Path $userDir, $cdnDir | Out-Null
Copy-Item (Join-Path $hosting $setupName) $userDir -Force
foreach ($name in @($manifestName, $fontName, $modName, $companionName)) {
    Copy-Item (Join-Path $hosting $name) $cdnDir -Force
}
Copy-Item (Join-Path $root 'DISTRIBUTION-RC35.md') $bundleRoot -Force

$checksumLines = New-Object System.Collections.Generic.List[string]
foreach ($name in @($setupName)) {
    $hash = Get-LowerSha256 (Join-Path $userDir $name)
    $checksumLines.Add("$hash  USER-DOWNLOAD/$name")
}
foreach ($name in @($manifestName, $fontName, $modName, $companionName)) {
    $hash = Get-LowerSha256 (Join-Path $cdnDir $name)
    $checksumLines.Add("$hash  CDN-UPLOAD/$name")
}
Set-Content (Join-Path $bundleRoot 'SHA256SUMS.txt') $checksumLines -Encoding ASCII

Write-Host '[5/5] Creating final distribution archive...'
Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $bundleZip -CompressionLevel Optimal
if (-not (Test-Path $bundleZip)) { throw "Distribution ZIP was not produced: $bundleZip" }
Write-Host "[OK] RC35 distribution bundle: $bundleZip"
Write-Host "[UPLOAD] Upload every file in $cdnDir to CloudFront origin /releases/."
Write-Host "[DISTRIBUTE] Give users only $(Join-Path $userDir $setupName)."
