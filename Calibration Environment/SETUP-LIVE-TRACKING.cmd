@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch.ps1" -SetupTracking
set "CALIBRATION_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %CALIBRATION_EXIT%
