@echo off
rem Launch the synthetic mouse/keyboard game using repository-relative paths.
setlocal
set "desktopPlayer=%~dp0build\desktop-pipeline\ELTS-Synthetic.exe"
if not exist "%desktopPlayer%" (
  echo Player not found. Run scripts\build.ps1 -Output build/desktop-pipeline.
  pause
  exit /b 1
)
start "ELTS mouse and keyboard test" /D "%~dp0build\desktop-pipeline" "%desktopPlayer%" -screen-fullscreen 0 -screen-width 1440 -screen-height 960
