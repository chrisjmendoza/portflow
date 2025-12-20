@echo off
setlocal

rem PortFlowBackup shipping uninstaller
rem Removes scheduled task and deletes C:\ProgramData\PortFlowBackup.

set "INSTALLDIR=C:\ProgramData\PortFlowBackup"

rem Ensure admin elevation (standard pattern)
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
  exit /b 0
)

rem Remove scheduled task (ignore if missing)
rem First, stop any running instance so files can be removed.
schtasks /End /TN "PortFlowBackup" >nul 2>&1
taskkill /F /IM "PortFlow.Runner.exe" /T >nul 2>&1

schtasks /Delete /TN "PortFlowBackup" /F >nul 2>&1

rem Remove installed files (ignore if already removed)
rem If user runs this from inside %INSTALLDIR%, a delayed cleanup is needed.
set "CLEANUP=%TEMP%\PortFlowBackup.cleanup.%RANDOM%.cmd"
(
  echo @echo off
  echo schtasks /End /TN "PortFlowBackup" ^>nul 2^>^&1
  echo taskkill /F /IM "PortFlow.Runner.exe" /T ^>nul 2^>^&1
  echo schtasks /Delete /TN "PortFlowBackup" /F ^>nul 2^>^&1
  echo timeout /t 2 /nobreak ^>nul
  echo rmdir /s /q "%INSTALLDIR%" ^>nul 2^>^&1
  echo del /f /q "%%~f0" ^>nul 2^>^&1
)>"%CLEANUP%" 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList '/c', ('""' + $env:CLEANUP + '""')" >nul 2>&1

rem Fallback: if we're not running from inside %INSTALLDIR%, this may succeed immediately.
rmdir /s /q "%INSTALLDIR%" >nul 2>&1

exit /b 0
