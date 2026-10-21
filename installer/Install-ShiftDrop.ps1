# ShiftDrop installer: builds from source, adds FFmpeg + PDFium + ONNX Runtime, installs for the current user (no admin).
# Run it with Install.cmd (double-click) or:  powershell -ExecutionPolicy Bypass -File installer\Install-ShiftDrop.ps1
param(
    [switch]$NoLaunch,    # don't start ShiftDrop at the end
    [switch]$SkipBuild,   # reuse build\publish
    [switch]$NoInstall,   # build and fetch tools only (used by CI)
    [switch]$ToolsOnly    # only fetch FFmpeg + PDFium into build\publish\tools (used by CI tests)
)

# 'Continue' on purpose: in Windows PowerShell, 'Stop' turns any stderr line of a native tool into a fatal error.
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'   # makes Invoke-WebRequest much faster
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root      = Split-Path -Parent $PSScriptRoot
$Cache     = Join-Path $PSScriptRoot 'cache'
$Publish   = Join-Path $Root 'build\publish'
$InstallTo = Join-Path $env:LOCALAPPDATA 'Programs\ShiftDrop'
$LogFile   = Join-Path $Root 'build\install.log'

$FfmpegUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip'
$PdfiumUrl = 'https://github.com/bblanchon/pdfium-binaries/releases/latest/download/pdfium-win-x64.tgz'
$OnnxVersion = '1.23.2'
$OnnxUrl   = "https://github.com/microsoft/onnxruntime/releases/download/v$OnnxVersion/onnxruntime-win-x64-$OnnxVersion.zip"
$ModelUrl  = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/silueta.onnx'

New-Item -ItemType Directory -Force -Path $Cache, (Split-Path $LogFile) | Out-Null
Start-Transcript -Path $LogFile -Force | Out-Null

# The console can't show Arabic properly, so messages here are English; $ar documents the meaning.
function Say($en, $ar) { Write-Host ''; Write-Host "==> $en" -ForegroundColor Cyan }
function Fail($en, $ar) {
    Write-Host ''; Write-Host "XX  $en" -ForegroundColor Red
    Write-Host "    Log: $LogFile"
    Stop-Transcript | Out-Null
    Read-Host 'Press Enter to close'
    exit 1
}

if (-not [Environment]::Is64BitOperatingSystem) { Fail 'ShiftDrop needs 64-bit Windows.' 'ShiftDrop يحتاج Windows بنسخة 64 بت.' }

# ---------------------------------------------------------------- .NET SDK
function Get-DotnetExe {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) {
            $sdks = & $c --list-sdks 2>$null
            if ($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 }) { return $c }
        }
    }
    return $null
}

if (-not $SkipBuild -and -not $ToolsOnly) {
    Say 'Checking the .NET 10 SDK...' 'التحقق من وجود .NET 10 SDK...'
    $dotnet = Get-DotnetExe
    if (-not $dotnet) {
        Say 'Installing the .NET 10 SDK (one time, about 250 MB)...' 'تثبيت .NET 10 SDK (مرة واحدة، حوالي 250 ميغا)...'
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if ($winget) {
            & winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements | Out-Host
            $dotnet = Get-DotnetExe
        }
        if (-not $dotnet) {
            $script = Join-Path $Cache 'dotnet-install.ps1'
            Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing -ErrorAction SilentlyContinue
            & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Channel 10.0 -InstallDir (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') | Out-Host
            $dotnet = Get-DotnetExe
        }
        if (-not $dotnet) { Fail 'Could not install the .NET 10 SDK. Install it from https://dotnet.microsoft.com/download and run this again.' 'تعذّر تثبيت .NET 10 SDK. ثبّته من الرابط ثم أعد تشغيل هذا الملف.' }
    }
    Write-Host "    $dotnet"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'

    # ------------------------------------------------------------ build
    Say 'Building ShiftDrop...' 'بناء ShiftDrop...'
    if (Test-Path $Publish) { Remove-Item -Recurse -Force $Publish }
    & $dotnet publish (Join-Path $Root 'src\ShiftDrop\ShiftDrop.csproj') -c Release -r win-x64 --self-contained true -o $Publish -p:DebugType=none | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $Publish 'ShiftDrop.exe'))) {
        Fail 'The build failed. Send build\install.log to Claude.' 'فشل البناء. أرسل الملف build\install.log إلى Claude.'
    }
}

# ---------------------------------------------------------------- FFmpeg + PDFium
$Tools = Join-Path $Publish 'tools'
New-Item -ItemType Directory -Force -Path $Tools | Out-Null

function Download($url, $file) {
    $path = Join-Path $Cache $file
    if (-not (Test-Path $path) -or ((Get-Item $path).Length -lt 100000)) {
        Invoke-WebRequest $url -OutFile "$path.part" -UseBasicParsing -ErrorAction Stop
        Move-Item -Force "$path.part" $path -ErrorAction Stop
    }
    return $path
}

Say 'Getting FFmpeg (LGPL build, about 180 MB, downloaded once)...' 'تنزيل FFmpeg (حوالي 180 ميغا، مرة واحدة)...'
try {
    $zip = Download $FfmpegUrl 'ffmpeg-win64-lgpl.zip'
    $tmp = Join-Path $Cache 'ffmpeg-x'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    & tar.exe -xf $zip -C $tmp
    $bin = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $bin) { Expand-Archive -Path $zip -DestinationPath $tmp -Force -ErrorAction Stop; $bin = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1 }
    if (-not $bin) { throw 'ffmpeg.exe not found in the download' }
    Copy-Item (Join-Path $bin.DirectoryName 'ffmpeg.exe') $Tools -Force -ErrorAction Stop
    Copy-Item (Join-Path $bin.DirectoryName 'ffprobe.exe') $Tools -Force -ErrorAction Stop
    $version = (& (Join-Path $Tools 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
    "$version`r`nFrom: $FfmpegUrl`r`nDownloaded: $((Get-Item $zip).LastWriteTime)`r`nLicence: LGPL 2.1+. Source: https://github.com/BtbN/FFmpeg-Builds and https://ffmpeg.org" |
        Set-Content -Encoding UTF8 (Join-Path $Tools 'ffmpeg-build.txt')
    if (Test-Path (Join-Path $bin.DirectoryName '..\LICENSE.txt')) { Copy-Item (Join-Path $bin.DirectoryName '..\LICENSE.txt') (Join-Path $Tools 'FFMPEG-LICENSE.txt') -Force }
    Remove-Item -Recurse -Force $tmp
} catch {
    Fail "Could not get FFmpeg: $($_.Exception.Message)" 'تعذّر تنزيل FFmpeg. تأكد من الاتصال بالإنترنت وأعد المحاولة.'
}

Say 'Getting PDFium...' 'تنزيل PDFium...'
try {
    $tgz = Download $PdfiumUrl 'pdfium-win-x64.tgz'
    $tmp = Join-Path $Cache 'pdfium-x'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    & tar.exe -xzf $tgz -C $tmp
    Copy-Item (Join-Path $tmp 'bin\pdfium.dll') $Tools -Force -ErrorAction Stop
    if (Test-Path (Join-Path $tmp 'LICENSE')) { Copy-Item (Join-Path $tmp 'LICENSE') (Join-Path $Tools 'PDFIUM-LICENSE.txt') -Force }
    Remove-Item -Recurse -Force $tmp
} catch {
    Fail "Could not get PDFium: $($_.Exception.Message)" 'تعذّر تنزيل PDFium. تأكد من الاتصال بالإنترنت وأعد المحاولة.'
}

# Optional: background removal (ONNX Runtime + the U2-Net "silueta" model). ShiftDrop works without it.
Say 'Getting ONNX Runtime and the background model...' 'تنزيل ONNX Runtime ونموذج إزالة الخلفية...'
try {
    $zip = Download $OnnxUrl "onnxruntime-win-x64-$OnnxVersion.zip"
    $tmp = Join-Path $Cache 'onnx-x'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    & tar.exe -xf $zip -C $tmp
    $dll = Get-ChildItem -Path $tmp -Recurse -Filter 'onnxruntime.dll' | Select-Object -First 1
    if (-not $dll) { throw 'onnxruntime.dll not found in the download' }
    Copy-Item $dll.FullName $Tools -Force -ErrorAction Stop
    $lic = Get-ChildItem -Path $tmp -Recurse -Filter 'LICENSE*' | Select-Object -First 1
    if ($lic) { Copy-Item $lic.FullName (Join-Path $Tools 'ONNXRUNTIME-LICENSE.txt') -Force }
    Remove-Item -Recurse -Force $tmp
    $model = Download $ModelUrl 'silueta.onnx'
    Copy-Item $model (Join-Path $Tools 'silueta.onnx') -Force -ErrorAction Stop
} catch {
    Write-Host "    Background removal will be off: $($_.Exception.Message)" -ForegroundColor Yellow
}

if ($ToolsOnly -or $NoInstall) {
    Say 'Build and tools are ready in build\publish.' 'البناء والأدوات جاهزة.'
    Stop-Transcript | Out-Null
    exit 0
}

# ---------------------------------------------------------------- install
Say "Installing to $InstallTo ..." 'التثبيت في مجلد البرامج الخاص بك...'
$running = Get-Process -Name ShiftDrop -ErrorAction SilentlyContinue
if ($running) {
    $exe = Join-Path $InstallTo 'ShiftDrop.exe'
    if (Test-Path $exe) { Start-Process $exe -ArgumentList '--quit' -Wait -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
    Get-Process -Name ShiftDrop -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Force -Path $InstallTo | Out-Null
& robocopy $Publish $InstallTo /MIR /NFL /NDL /NJH /NJS /NP /R:3 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { Fail 'Copying the files failed.' 'فشل نسخ الملفات.' }
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-ShiftDrop.ps1') $InstallTo -Force
if (-not (Test-Path (Join-Path $InstallTo 'ShiftDrop.exe'))) { Fail 'ShiftDrop.exe is missing after copying.' 'الملف غير موجود بعد النسخ.' }

$exe = Join-Path $InstallTo 'ShiftDrop.exe'
$shell = New-Object -ComObject WScript.Shell
function Shortcut($path, $arguments, $desc) {
    $s = $shell.CreateShortcut($path)
    $s.TargetPath = $exe
    $s.Arguments = $arguments
    $s.WorkingDirectory = $InstallTo
    $s.IconLocation = "$exe,0"
    $s.Description = $desc
    $s.Save()
}
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'ShiftDrop.lnk'
Shortcut $startMenu '' 'ShiftDrop: drag a file, press Shift'
$sendTo = Join-Path ([Environment]::GetFolderPath('SendTo')) 'ShiftDrop.lnk'
Shortcut $sendTo '' 'Convert with ShiftDrop'

# start with Windows
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ShiftDrop' -Value "`"$exe`""

# Apps & features entry (per user)
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ShiftDrop'
New-Item -Path $uninstallKey -Force | Out-Null
$version = (Get-Item $exe).VersionInfo.ProductVersion
Set-ItemProperty $uninstallKey -Name DisplayName -Value 'ShiftDrop'
Set-ItemProperty $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty $uninstallKey -Name Publisher -Value 'ShiftDrop (open source)'
Set-ItemProperty $uninstallKey -Name DisplayIcon -Value $exe
Set-ItemProperty $uninstallKey -Name InstallLocation -Value $InstallTo
Set-ItemProperty $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallTo\Uninstall-ShiftDrop.ps1`""
Set-ItemProperty $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $uninstallKey -Name NoRepair -Value 1 -Type DWord
$size = [int]((Get-ChildItem $InstallTo -Recurse | Measure-Object -Property Length -Sum).Sum / 1KB)
Set-ItemProperty $uninstallKey -Name EstimatedSize -Value $size -Type DWord

Say 'Done! ShiftDrop is running in the tray, near the clock.' 'تم! ShiftDrop يعمل الآن في شريط المهام بجانب الساعة.'
Write-Host '    Drag any file in Explorer and press Shift.'
Stop-Transcript | Out-Null
if (-not $NoLaunch) { Start-Process $exe }
Start-Sleep -Seconds 4
