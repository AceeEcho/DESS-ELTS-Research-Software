@echo off
setlocal
rem Development entry point: keep failures visible when opened from Explorer.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%PSModulePath%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-dev.ps1" %*
set "ELTS_START_EXIT=%ERRORLEVEL%"
if not "%ELTS_START_EXIT%"=="0" (
  echo.
  echo ELTS could not finish starting. Read the failed step and How to resolve instructions above.
  echo Startup log: "%~dp0diagnostics\start-console.log"
  echo Troubleshooting: "%~dp0docs\operator\multi-machine-setup.md"
  pause
)
exit /b %ELTS_START_EXIT%
