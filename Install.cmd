@echo off
rem Double-click to build and install ShiftDrop for this Windows user (no admin needed).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install-ShiftDrop.ps1" %*
