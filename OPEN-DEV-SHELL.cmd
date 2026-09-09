@echo off
rem No permanent PATH changes: this shell inherits the local setup configuration.
powershell.exe -NoProfile -NoExit -ExecutionPolicy Bypass -File "%~dp0scripts\dev-shell.ps1"
