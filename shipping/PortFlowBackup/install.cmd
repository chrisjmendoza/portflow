@echo off
setlocal

rem PortFlowBackup shipping installer
rem Installs into C:\ProgramData\PortFlowBackup and registers scheduled task.

set "INSTALLDIR=C:\ProgramData\PortFlowBackup"
set "LOGDIR=%INSTALLDIR%\logs"
set "SRCDIR=%~dp0"
set "PF_USER=%USERDOMAIN%\%USERNAME%"

set "ELEVATED=0"
if /i "%~1"=="--elevated" (
  set "ELEVATED=1"
  if not "%~2"=="" set "PF_USER=%~2"
)

rem Ensure admin elevation (standard pattern)
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo.
  echo PortFlowBackup Installer
  echo =======================
  echo.
  echo Administrator access is required to install.
  echo If Windows prompts you, choose Yes.
  echo.
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WindowStyle Normal -ArgumentList '--elevated','%PF_USER%'" >nul 2>&1
  if not "%errorlevel%"=="0" (
    echo Failed to request administrator elevation.
    echo.
    pause
    exit /b 1
  )
  echo A new installer window should open.
  echo.
  pause
  exit /b 0
)

set "RC=0"

if not exist "%INSTALLDIR%" mkdir "%INSTALLDIR%" >nul 2>&1
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1

rem Copy binaries/templates (overwrite)
if not exist "%SRCDIR%PortFlow.Runner.exe" (
  set "RC=1"
  goto done
)
copy /y "%SRCDIR%PortFlow.Runner.exe" "%INSTALLDIR%\PortFlow.Runner.exe" >nul
if not "%errorlevel%"=="0" (
  set "RC=1"
  goto done
)

rem Copy docs and tooling (overwrite so users keep latest instructions)
if exist "%SRCDIR%README-SETUP.txt" copy /y "%SRCDIR%README-SETUP.txt" "%INSTALLDIR%\README-SETUP.txt" >nul
if exist "%SRCDIR%README-TROUBLESHOOTING.txt" copy /y "%SRCDIR%README-TROUBLESHOOTING.txt" "%INSTALLDIR%\README-TROUBLESHOOTING.txt" >nul
if exist "%SRCDIR%install.cmd" copy /y "%SRCDIR%install.cmd" "%INSTALLDIR%\install.cmd" >nul
if exist "%SRCDIR%uninstall.cmd" copy /y "%SRCDIR%uninstall.cmd" "%INSTALLDIR%\uninstall.cmd" >nul
if exist "%SRCDIR%install-task.ps1" copy /y "%SRCDIR%install-task.ps1" "%INSTALLDIR%\install-task.ps1" >nul

if exist "%SRCDIR%PORTFLOW_TARGET.txt" (
  copy /y "%SRCDIR%PORTFLOW_TARGET.txt" "%INSTALLDIR%\PORTFLOW_TARGET.txt" >nul
)

rem Preserve user-editable config across reinstalls
if not exist "%INSTALLDIR%\portflow.backup.json" (
  if not exist "%SRCDIR%portflow.backup.json" (
    set "RC=1"
    goto done
  )
  copy "%SRCDIR%portflow.backup.json" "%INSTALLDIR%\portflow.backup.json" >nul
  if not "%errorlevel%"=="0" (
    set "RC=1"
    goto done
  )
)

rem Register / replace scheduled task
powershell -NoProfile -ExecutionPolicy Bypass -File "%SRCDIR%install-task.ps1" -InstallDir "%INSTALLDIR%" -UserId "%PF_USER%" >nul 2>&1
if not "%errorlevel%"=="0" (
  set "RC=1"
  goto done
)

rem Optional install log (best-effort)
(
  echo %date% %time% Installed PortFlowBackup to %INSTALLDIR%
)>>"%LOGDIR%\install.log" 2>nul

:done
echo.
echo PortFlowBackup Installer
echo =======================
echo.
if "%RC%"=="0" (
  echo Install complete.
  echo Installed to: %INSTALLDIR%\
  echo Scheduled task: PortFlowBackup
) else (
  echo INSTALL FAILED.
  echo Please re-run install.cmd as Administrator.
  echo.
  echo If this keeps failing, check:
  echo   %LOGDIR%\install.log
)
echo.
pause
exit /b %RC%
