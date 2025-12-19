@echo off
echo.
echo PortFlow USB Backup - Uninstall
echo ==================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass ^
  -Command "Unregister-ScheduledTask -TaskName 'PortFlow USB Backup' -Confirm:$false"

if %ERRORLEVEL% NEQ 0 (
  echo.
  echo Uninstall failed or task not found.
  pause
  exit /b 1
)

echo.
echo PortFlow USB Backup has been removed.
pause
