@echo off
setlocal
rem No permanent PATH changes: this shell inherits the local setup configuration.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%PSModulePath%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NoExit -ExecutionPolicy Bypass -File "%~dp0scripts\dev-shell.ps1"
