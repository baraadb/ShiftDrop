# Removes ShiftDrop for the current user. Your converted files are not touched.
$ErrorActionPreference = 'SilentlyContinue'
$InstallTo = Join-Path $env:LOCALAPPDATA 'Programs\ShiftDrop'
$exe = Join-Path $InstallTo 'ShiftDrop.exe'

if (Test-Path $exe) { Start-Process $exe -ArgumentList '--quit' -Wait }
Start-Sleep -Milliseconds 800
Get-Process -Name ShiftDrop | Stop-Process -Force

Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'ShiftDrop.lnk') -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('SendTo')) 'ShiftDrop.lnk') -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ShiftDrop'
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ShiftDrop' -Recurse -Force

# the script lives in the folder it deletes, so finish from a temporary copy of cmd
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$InstallTo`"" -WindowStyle Hidden
Write-Host 'ShiftDrop was removed. Settings stay in %APPDATA%\ShiftDrop (delete that folder too if you like).'
Start-Sleep -Seconds 3
