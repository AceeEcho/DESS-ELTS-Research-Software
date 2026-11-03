@echo off
setlocal
rem Run from the extracted project folder; no Git or Unity installation is needed.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%PSModulePath%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-tracking.ps1" %*
set "ELTS_TRACKING_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %ELTS_TRACKING_EXIT%
