@echo off
setlocal
rem Independent calibration workspace. This does not start ELTS or Unity.
rem The first start offers the one-time live-tracking setup; manual preview works without it.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%PSModulePath%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Calibration Environment\launch.ps1" %*
set "CALIBRATION_EXIT=%ERRORLEVEL%"
if not "%CALIBRATION_EXIT%"=="0" pause
exit /b %CALIBRATION_EXIT%
