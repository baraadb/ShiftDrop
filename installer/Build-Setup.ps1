# Makes build\ShiftDrop-Setup-<version>.exe: one file to give to anyone. No .NET needed on their PC.
# Always rebuilds the app first (FFmpeg and PDFium come from the download cache), so the installer has the latest code.
param([switch]$SkipBuild)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$Root = Split-Path -Parent $PSScriptRoot
$Publish = Join-Path $Root 'build\publish'

function Say($t) { Write-Host ''; Write-Host "==> $t" -ForegroundColor Cyan }
function Fail($t) { Write-Host ''; Write-Host "XX  $t" -ForegroundColor Red; Read-Host 'Press Enter to close'; exit 1 }

$ready = (Test-Path (Join-Path $Publish 'ShiftDrop.exe')) -and (Test-Path (Join-Path $Publish 'tools\ffmpeg.exe')) -and (Test-Path (Join-Path $Publish 'tools\pdfium.dll'))
if (-not $SkipBuild -or -not $ready) {
    Say 'Building ShiftDrop and fetching FFmpeg + PDFium...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Install-ShiftDrop.ps1') -NoInstall -NoLaunch
    if (-not (Test-Path (Join-Path $Publish 'ShiftDrop.exe'))) { Fail 'The build did not finish. See build\install.log.' }
}

Say 'Finding Inno Setup...'
function Find-Iscc {
    foreach ($p in @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}
$iscc = Find-Iscc
if (-not $iscc) {
    Say 'Installing Inno Setup (free, one time)...'
    & winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements | Out-Host
    $iscc = Find-Iscc
}
if (-not $iscc) { Fail 'Inno Setup is not installed. Get it from https://jrsoftware.org/isdl.php and run this again.' }

$version = (Get-Item (Join-Path $Publish 'ShiftDrop.exe')).VersionInfo.ProductVersion
if ($version) { $version = $version.Split('+')[0] } else { $version = '0.1.0' }

Say "Making ShiftDrop-Setup-$version.exe (compression takes a minute)..."
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ShiftDrop.iss') | Out-Host
$setup = Join-Path $Root "build\ShiftDrop-Setup-$version.exe"
if (-not (Test-Path $setup)) { Fail 'Inno Setup did not produce the installer.' }

$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Say "Done: $setup ($mb MB)"
Start-Process explorer.exe "/select,`"$setup`""
Start-Sleep -Seconds 3
