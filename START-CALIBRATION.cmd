@echo off
rem Independent calibration workspace. This does not start ELTS or Unity.
call "%~dp0Calibration Environment\START-CALIBRATION.cmd" %*
exit /b %ERRORLEVEL%
