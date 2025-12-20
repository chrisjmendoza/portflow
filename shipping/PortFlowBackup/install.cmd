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
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '--elevated','%PF_USER%'" >nul 2>&1
  exit /b 0
)

if not exist "%INSTALLDIR%" mkdir "%INSTALLDIR%" >nul 2>&1
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1

rem Copy binaries/templates (overwrite)
if not exist "%SRCDIR%PortFlow.Runner.exe" exit /b 1
copy /y "%SRCDIR%PortFlow.Runner.exe" "%INSTALLDIR%\PortFlow.Runner.exe" >nul

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
  if not exist "%SRCDIR%portflow.backup.json" exit /b 1
  copy "%SRCDIR%portflow.backup.json" "%INSTALLDIR%\portflow.backup.json" >nul
)

rem Register / replace scheduled task
powershell -NoProfile -ExecutionPolicy Bypass -File "%SRCDIR%install-task.ps1" -InstallDir "%INSTALLDIR%" -UserId "%PF_USER%" >nul 2>&1
if not "%errorlevel%"=="0" exit /b 1

rem Optional install log (best-effort)
(
  echo %date% %time% Installed PortFlowBackup to %INSTALLDIR%
)>>"%LOGDIR%\install.log" 2>nul

exit /b 0
