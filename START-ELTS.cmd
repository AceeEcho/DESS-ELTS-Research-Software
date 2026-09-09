@echo off
rem Study startup cannot silently launch a synthetic player.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-study.ps1" %*
exit /b %errorlevel%
