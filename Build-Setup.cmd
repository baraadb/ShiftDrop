@echo off
rem Double-click to make build\ShiftDrop-Setup-<version>.exe, a single installer to share.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Build-Setup.ps1" %*
