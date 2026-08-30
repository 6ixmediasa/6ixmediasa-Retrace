@echo off
setlocal
cd /d "%~dp0"
echo.
echo ========================================
echo       RETRACE V0.2.0 TEST INSTALLER
echo ========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-retrace.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Please send the error above back to ChatGPT.
  pause
  exit /b 1
)
echo.
echo Retrace installation finished.
pause
