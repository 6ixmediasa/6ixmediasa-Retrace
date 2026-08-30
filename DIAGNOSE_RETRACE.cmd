@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\diagnose-retrace.ps1"
echo.
pause
