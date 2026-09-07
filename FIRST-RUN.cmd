@echo off
rem Study provisioning remains gated; development setup uses scripts/bootstrap.ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-study-machine.ps1" %*
exit /b %errorlevel%
