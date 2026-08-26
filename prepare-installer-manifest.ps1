$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = '1.0.0-rc35'
$templatePath = Join-Path $root 'installer\installer-manifest.template.json'
$outDir = Join-Path $root 'release-hosting'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Download-And-Hash([string]$url, [string]$outFile) {
    Write-Host "[DOWNLOAD] $url"
    Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing
    $hash = (Get-FileHash $outFile -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "[SHA256] $(Split-Path $outFile -Leaf) = $hash"
    return $hash
}

$bepUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip'
$apiUrl = 'https://thunderstore.io/package/download/xhayper/COTL_API/0.3.4/'
$bepPath = Join-Path $outDir 'BepInEx_x64_5.4.21.0.zip'
$apiPath = Join-Path $outDir 'xhayper-COTL_API-0.3.4.zip'
$bepHash = Download-And-Hash $bepUrl $bepPath
$apiHash = Download-And-Hash $apiUrl $apiPath

$fontPath = Join-Path $outDir 'COTL-KoreanFontFix-4.2.1-rc35.zip'
$modPath = Join-Path $outDir "ChzzkOfTheLamb-Mod-$release.zip"
$companionPath = Join-Path $outDir "ChzzkOfTheLamb-Companion-$release-win-x64.zip"
foreach ($p in @($fontPath,$modPath,$companionPath)) {
    if (-not (Test-Path $p)) { throw "Missing release component: $p`nRun build-release.ps1 first." }
}
$fontHash = (Get-FileHash $fontPath -Algorithm SHA256).Hash.ToLowerInvariant()
$modHash = (Get-FileHash $modPath -Algorithm SHA256).Hash.ToLowerInvariant()
$companionHash = (Get-FileHash $companionPath -Algorithm SHA256).Hash.ToLowerInvariant()

$json = Get-Content $templatePath -Raw
$json = $json.Replace('__BEPINEX_SHA256__', $bepHash)
$json = $json.Replace('__COTL_API_SHA256__', $apiHash)
$json = $json.Replace('__FONT_FIX_SHA256__', $fontHash)
$json = $json.Replace('__MOD_SHA256__', $modHash)
$json = $json.Replace('__COMPANION_SHA256__', $companionHash)
if ($json -match '__[A-Z0-9_]+__') { throw 'Installer manifest still contains an unresolved placeholder.' }
$parsed = $json | ConvertFrom-Json
if ($parsed.release -ne $release) { throw "Unexpected manifest release: $($parsed.release)" }
$outManifest = Join-Path $outDir "installer-manifest-$release.json"
[System.IO.File]::WriteAllText($outManifest, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[OK] Installer manifest: $outManifest"
Write-Host "[NEXT] Upload your own component ZIPs + installer-manifest-$release.json under CloudFront /releases/."
Write-Host '[NOTE] BepInEx and COTL_API remain downloaded from their official providers by end-user installer.'
