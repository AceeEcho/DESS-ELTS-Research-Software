@echo off
rem Launch the locally built synthetic dashboard using repository-relative paths.
setlocal
set "dashboardPlayer=%~dp0build\operator-dashboard\ELTS-Synthetic.exe"
if not exist "%dashboardPlayer%" (
  echo Dashboard player not found. Build it with scripts\build.ps1 -Output build/operator-dashboard.
  pause
  exit /b 1
)
start "ELTS administrator dashboard" /D "%~dp0build\operator-dashboard" "%dashboardPlayer%" -screen-fullscreen 0 -screen-width 1440 -screen-height 960
