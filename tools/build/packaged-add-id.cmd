@echo off
setlocal
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0start-rehearsal.ps1" -ChooseParticipantId
if errorlevel 1 pause
