@echo off
setlocal

rem PortFlowBackup shipping installer
rem Installs into C:\ProgramData\PortFlowBackup and registers scheduled task.

set "INSTALLDIR=C:\ProgramData\PortFlowBackup"
set "LOGDIR=%INSTALLDIR%\logs"
set "INSTALLLOG=%LOGDIR%\install.log"
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
set "FAIL_REASON="

if not exist "%INSTALLDIR%" mkdir "%INSTALLDIR%" >nul 2>&1
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1

(
  echo.
  echo =======================
  echo %date% %time% Install started
  echo InstallDir: %INSTALLDIR%
  echo SourceDir : %SRCDIR%
  echo UserId    : %PF_USER%
  echo =======================
)>>"%INSTALLLOG%" 2>nul

rem Copy binaries/templates (overwrite)
if not exist "%SRCDIR%PortFlow.Runner.exe" (
  set "RC=1"
  set "FAIL_REASON=PortFlow.Runner.exe not found in source folder"
  goto done
)
copy /y "%SRCDIR%PortFlow.Runner.exe" "%INSTALLDIR%\PortFlow.Runner.exe" >nul
if not "%errorlevel%"=="0" (
  set "RC=1"
  set "FAIL_REASON=Failed to copy PortFlow.Runner.exe"
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
    set "FAIL_REASON=portflow.backup.json not found in source folder"
    goto done
  )
  copy "%SRCDIR%portflow.backup.json" "%INSTALLDIR%\portflow.backup.json" >nul
  if not "%errorlevel%"=="0" (
    set "RC=1"
    set "FAIL_REASON=Failed to copy portflow.backup.json"
    goto done
  )
)

rem Register / replace scheduled task
powershell -NoProfile -ExecutionPolicy Bypass -File "%SRCDIR%install-task.ps1" -InstallDir "%INSTALLDIR%" -UserId "%PF_USER%" >>"%INSTALLLOG%" 2>&1
if not "%errorlevel%"=="0" (
  set "RC=1"
  set "FAIL_REASON=Scheduled task registration failed (see install.log)"
  goto done
)

(
  echo %date% %time% Scheduled task registered successfully.
)>>"%INSTALLLOG%" 2>nul

:done
(
  if "%RC%"=="0" (
    echo %date% %time% Install result: SUCCESS
  ) else (
    echo %date% %time% Install result: FAILURE
    if not "%FAIL_REASON%"=="" echo Reason: %FAIL_REASON%
  )
)>>"%INSTALLLOG%" 2>nul

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
  if not "%FAIL_REASON%"=="" echo Reason: %FAIL_REASON%
  echo.
  echo If this keeps failing, check:
  echo   %INSTALLLOG%
)
echo.
pause
exit /b %RC%
