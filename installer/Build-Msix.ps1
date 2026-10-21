# Makes build\ShiftDrop-<version>.msix for the Microsoft Store from build\publish.
# The Store signs the package itself, so nothing is signed here. Needs the Windows 10/11 SDK (makeappx, makepri).
param([string]$Publish)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Publish) { $Publish = Join-Path $Root 'build\publish' }
$Exe = Join-Path $Publish 'ShiftDrop.exe'
if (-not (Test-Path $Exe)) { throw "Build ShiftDrop first: $Exe is missing." }

function Find-SdkTool($name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -Path $kits -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $tool) { throw "$name not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }
    return $tool.FullName
}
$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'

$version = (Get-Item $Exe).VersionInfo.ProductVersion.Split('+')[0]
$parts = @($version.Split('.')) + @('0', '0', '0')
$msixVersion = '{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]   # the Store wants the last part to be 0

$store = Get-Content (Join-Path $PSScriptRoot 'msix\store.json') -Raw | ConvertFrom-Json

# layout: the published app + Store images + the filled-in manifest
$Layout = Join-Path $Root 'build\msix'
if (Test-Path $Layout) { Remove-Item -Recurse -Force $Layout }
New-Item -ItemType Directory -Force -Path $Layout | Out-Null
Copy-Item -Path (Join-Path $Publish '*') -Destination $Layout -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'msix\Assets') -Destination (Join-Path $Layout 'Assets') -Recurse -Force
$manifest = Get-Content (Join-Path $PSScriptRoot 'msix\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('$IdentityName$', $store.IdentityName).Replace('$Publisher$', $store.Publisher).
    Replace('$PublisherDisplayName$', $store.PublisherDisplayName).Replace('$Version$', $msixVersion)
[IO.File]::WriteAllText((Join-Path $Layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# resources.pri lets Windows pick the right image size for each screen
$pri = Join-Path $Root 'build\priconfig.xml'
& $makepri createconfig /cf $pri /dq en-US /pv 10.0.0 /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed' }
& $makepri new /pr $Layout /cf $pri /mn (Join-Path $Layout 'AppxManifest.xml') /of (Join-Path $Layout 'resources.pri') /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed' }

$out = Join-Path $Root "build\ShiftDrop-$version.msix"
& $makeappx pack /d $Layout /p $out /o | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw 'makeappx failed' }
Write-Host "==> $out ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB, package version $msixVersion)"
