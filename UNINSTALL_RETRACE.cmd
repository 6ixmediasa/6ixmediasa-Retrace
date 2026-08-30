@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\Programs\Retrace\uninstall-retrace.ps1"
echo Retrace has been uninstalled. Recovery data was kept in %%LOCALAPPDATA%%\Retrace.
pause
