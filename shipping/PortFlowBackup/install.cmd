@echo off
echo.
echo PortFlow USB Backup - Installer
echo ==================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass ^
  -File "%~dp0install-task.ps1"

if %ERRORLEVEL% NEQ 0 (
  echo.
  echo Installation failed.
  echo Please run this file as Administrator.
  pause
  exit /b 1
)

echo.
echo Installation complete.
echo The backup will now run automatically when you log in.
pause
