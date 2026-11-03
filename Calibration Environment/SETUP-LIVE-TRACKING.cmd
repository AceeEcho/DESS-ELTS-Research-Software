@echo off
setlocal
rem Keep the original shortcut working; both entry points run full setup.
call "%~dp0..\SETUP-TRACKING.cmd" %*
exit /b %ERRORLEVEL%
