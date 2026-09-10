@echo off
rem Launch the synthetic mouse/keyboard game using repository-relative paths.
setlocal
set "desktopPlayer=%~dp0build\test-station-v2\ELTS-Synthetic.exe"
if not exist "%desktopPlayer%" (
  echo Player not found. Run scripts\build.ps1 -Output build/test-station-v2.
  pause
  exit /b 1
)
start "ELTS participant test" /D "%~dp0build\test-station-v2" "%desktopPlayer%"
