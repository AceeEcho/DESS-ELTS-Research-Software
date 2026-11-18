@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-rehearsal.ps1"
if errorlevel 1 pause
