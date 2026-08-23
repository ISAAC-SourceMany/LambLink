param(
    [string]$GamePath = '',
    [switch]$NoDesktopShortcut
)
$ErrorActionPreference = 'Stop'

function Find-SteamRoot {
    $candidates = @()
    try { $candidates += (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch {}
    try { $candidates += (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction Stop).InstallPath } catch {}
    foreach ($p in $candidates) { if ($p -and (Test-Path $p)) { return $p } }
    return $null
}

function Find-CotlPath {
    param([string]$SteamRoot)
    $libraries = New-Object System.Collections.Generic.List[string]
    if ($SteamRoot) { $libraries.Add($SteamRoot) }
    $vdf = if ($SteamRoot) { Join-Path $SteamRoot 'steamapps\libraryfolders.vdf' } else { $null }
    if ($vdf -and (Test-Path $vdf)) {
        foreach ($line in Get-Content $vdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraries.Add(($matches[1] -replace '\\\\','\'))
            }
        }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $candidate = Join-Path $lib 'steamapps\common\Cult of the Lamb'
        if (Test-Path (Join-Path $candidate 'Cult Of The Lamb.exe')) { return $candidate }
    }
    return $null
}

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $GamePath) { $GamePath = Find-CotlPath (Find-SteamRoot) }
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'Cult Of The Lamb.exe'))) {
    throw 'Cult of the Lamb 설치 경로를 자동으로 찾지 못했습니다. -GamePath "C:\...\Cult of the Lamb" 옵션으로 다시 실행해주세요.'
}

$bepinex = Join-Path $GamePath 'BepInEx'
if (-not (Test-Path $bepinex)) {
    throw 'BepInEx가 설치되어 있지 않습니다. 현재 릴리즈 설치기는 기존 BepInEx 설치를 전제로 합니다.'
}
$apiFound = Get-ChildItem (Join-Path $bepinex 'plugins') -Recurse -Filter 'COTL_API.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $apiFound) {
    throw 'COTL_API.dll을 찾지 못했습니다. 현재 릴리즈 설치기는 기존 COTL_API 설치를 전제로 합니다.'
}

$pluginSource = Join-Path $packageRoot 'Plugin'
$companionSource = Join-Path $packageRoot 'Companion'
$fontSource = Join-Path $packageRoot 'BundledMods\COTL_KoreanFontFix'
if (-not (Test-Path (Join-Path $pluginSource 'ChzzkOfTheLamb.Mod.dll'))) { throw 'Plugin 폴더의 Mod DLL이 없습니다.' }
if (-not (Test-Path (Join-Path $companionSource 'ChzzkOfTheLamb.Companion.exe'))) { throw 'Companion 실행 파일이 없습니다.' }
if (-not (Test-Path (Join-Path $fontSource 'COTL_KoreanFontFix.dll'))) { throw '번들된 COTL Korean Font Fix DLL이 없습니다. 배포 ZIP이 불완전합니다.' }
if (-not (Test-Path (Join-Path $fontSource 'koreanfont.bundle'))) { throw '번들된 koreanfont.bundle이 없습니다. 배포 ZIP이 불완전합니다.' }

$pluginDest = Join-Path $bepinex 'plugins\ChzzkOfTheLamb'
$fontDest = Join-Path $bepinex 'plugins\COTL_KoreanFontFix'
$companionDest = Join-Path $env:LOCALAPPDATA 'Programs\ChzzkOfTheLamb'
New-Item -ItemType Directory -Force -Path $pluginDest, $fontDest, $companionDest | Out-Null
Copy-Item (Join-Path $pluginSource '*') $pluginDest -Recurse -Force
Copy-Item (Join-Path $fontSource '*') $fontDest -Recurse -Force
Copy-Item (Join-Path $companionSource '*') $companionDest -Recurse -Force

# Verify exact font patch layout used by the tested 4.2.1 runtime.
$installedFontDll = Join-Path $fontDest 'COTL_KoreanFontFix.dll'
$installedFontBundle = Join-Path $fontDest 'koreanfont.bundle'
if (-not (Test-Path $installedFontDll) -or -not (Test-Path $installedFontBundle)) {
    throw '한글 폰트 패치 설치 검증에 실패했습니다.'
}

if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutPath = Join-Path $desktop 'ChzzkOfTheLamb Companion.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $companionDest 'ChzzkOfTheLamb.Companion.exe'
    $shortcut.WorkingDirectory = $companionDest
    $shortcut.Save()
}

Write-Host ''
Write-Host '설치 완료.' -ForegroundColor Green
Write-Host "Game Mod: $pluginDest"
Write-Host "Korean Font Fix: $fontDest"
Write-Host "Companion: $companionDest"
Write-Host '게임을 실행한 뒤 Companion을 실행하고 브라우저에서 CHZZK 연결을 승인하세요.'
