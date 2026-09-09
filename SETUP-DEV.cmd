@echo off
setlocal
rem Keep the window open so a first-time user can read success or failure.
rem A parent PowerShell 7 session can export an incompatible module search path.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%PSModulePath%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-dev.ps1" %*
set "ELTS_SETUP_EXIT=%ERRORLEVEL%"
echo.
if not "%ELTS_SETUP_EXIT%"=="0" echo Setup needs attention. Read the message above and docs\operator\multi-machine-setup.md.
pause
exit /b %ELTS_SETUP_EXIT%
