@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch.ps1" -StopApp %*
set "CALIBRATION_EXIT=%ERRORLEVEL%"
if not "%CALIBRATION_EXIT%"=="0" pause
exit /b %CALIBRATION_EXIT%
