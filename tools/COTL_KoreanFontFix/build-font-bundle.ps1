param(
    [Parameter(Mandatory = $true)]
    [string]$FontFile,

    [Parameter(Mandatory = $true)]
    [string]$UnityEditor
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot "FontBundleBuilder"
$log = Join-Path $scriptRoot "font-bundle-build.log"
$buildDir = Join-Path $project "Build"
$expectedBundle = Join-Path $buildDir "koreanfont.bundle"
$destinationBundle = Join-Path $scriptRoot "koreanfont.bundle"

if (-not (Test-Path -LiteralPath $UnityEditor)) {
    throw "Unity Editor was not found: $UnityEditor"
}

if (-not (Test-Path -LiteralPath $FontFile)) {
    throw "Source font was not found: $FontFile"
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "FontBundleBuilder project was not found: $project"
}

Write-Host "Unity Editor: $UnityEditor"
Write-Host "Source font: $FontFile"
Write-Host "Building Korean TMP AssetBundle..."

if (Test-Path -LiteralPath $buildDir) {
    Remove-Item -LiteralPath $buildDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $destinationBundle) {
    Remove-Item -LiteralPath $destinationBundle -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $log) {
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
}

$unityArgs = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $project,
    "-executeMethod", "KoreanFontBundleBuilder.BuildFromCommandLine",
    "-font", $FontFile,
    "-logFile", $log
)

$process = Start-Process `
    -FilePath $UnityEditor `
    -ArgumentList $unityArgs `
    -Wait `
    -PassThru `
    -NoNewWindow

$unityExitCode = $process.ExitCode

$deadline = (Get-Date).AddSeconds(30)
$success = $false
$failure = $false

while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $log) {
        try {
            $logText = Get-Content -LiteralPath $log -Raw -ErrorAction Stop
        } catch {
            $logText = ""
        }

        if ($logText -match "KOREAN_FONT_BUNDLE_FAILED") {
            $failure = $true
            break
        }

        if ($logText -match "KOREAN_FONT_BUNDLE_OK=") {
            $success = $true
            break
        }
    }

    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -LiteralPath $log)) {
    throw "Unity did not create the build log: $log"
}

$logText = Get-Content -LiteralPath $log -Raw

Write-Host ""

Select-String `
    -LiteralPath $log `
    -Pattern "KOREAN_FONT_SOURCE=|KOREAN_FONT_PREBAKE_START=|KOREAN_FONT_PREBAKE_RESULT=|KOREAN_FONT_PREBAKE_MISSING_COUNT=" `
    -CaseSensitive:$false |
    ForEach-Object {
        Write-Host $_.Line
    }

if ($failure -or $logText -match "KOREAN_FONT_BUNDLE_FAILED") {
    Select-String `
        -LiteralPath $log `
        -Pattern "KOREAN_FONT_|Exception|error CS|shader|failed" `
        -CaseSensitive:$false |
        Select-Object -Last 100 |
        ForEach-Object {
            Write-Host $_.Line
        }

    throw "Font bundle build failed. See: $log"
}

if (-not $success -and
    $logText -notmatch "KOREAN_FONT_BUNDLE_OK=") {
    throw "KOREAN_FONT_BUNDLE_OK marker was not found. See: $log"
}

$successLine =
    Select-String `
        -LiteralPath $log `
        -Pattern "KOREAN_FONT_BUNDLE_OK=" `
        -CaseSensitive:$false |
    Select-Object -Last 1

$reportedBundle = $null

if ($successLine) {
    $marker = "KOREAN_FONT_BUNDLE_OK="

    $idx =
        $successLine.Line.IndexOf(
            $marker,
            [System.StringComparison]::OrdinalIgnoreCase)

    if ($idx -ge 0) {
        $reportedBundle =
            $successLine.Line.Substring(
                $idx + $marker.Length).Trim()
    }
}

$sourceBundle = $null
$bundleDeadline = (Get-Date).AddSeconds(10)

while ((Get-Date) -lt $bundleDeadline) {
    if ($reportedBundle -and
        (Test-Path -LiteralPath $reportedBundle)) {
        $sourceBundle = $reportedBundle
        break
    }

    if (Test-Path -LiteralPath $expectedBundle) {
        $sourceBundle = $expectedBundle
        break
    }

    Start-Sleep -Milliseconds 250
}

if (-not $sourceBundle) {
    throw "Unity reported success, but koreanfont.bundle was not found."
}

$sourceInfo =
    Get-Item -LiteralPath $sourceBundle

if ($sourceInfo.Length -lt 65536) {
    throw "Generated koreanfont.bundle is suspiciously small: $($sourceInfo.Length) bytes"
}

Copy-Item `
    -LiteralPath $sourceBundle `
    -Destination $destinationBundle `
    -Force

$copiedInfo =
    Get-Item -LiteralPath $destinationBundle

Write-Host ""
Write-Host "Korean font bundle build completed successfully." -ForegroundColor Green
Write-Host "Unity exit code: $unityExitCode"
Write-Host "Built bundle: $sourceBundle"
Write-Host "Copied bundle: $destinationBundle"
Write-Host ("Bundle size: {0:N2} MB" -f ($copiedInfo.Length / 1MB))
Write-Host "Build log: $log"
Write-Host "Next step: .\build-plugin.ps1"
