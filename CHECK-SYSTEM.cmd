@echo off
rem Diagnostics report development readiness only, never physical acceptance.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\doctor.ps1" %*
exit /b %errorlevel%
