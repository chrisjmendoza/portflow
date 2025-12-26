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
set "FORCE_FRESH=0"
if /i "%~1"=="--elevated" (
  set "ELEVATED=1"
  if not "%~2"=="" set "PF_USER=%~2"
  if /i "%~3"=="--force" set "FORCE_FRESH=1"
)
if /i "%~1"=="--force" set "FORCE_FRESH=1"

rem Ensure admin elevation (standard pattern)
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo PortFlowBackup Installer
  echo =======================
  echo.
  echo Administrator access is required to install.
  echo If Windows prompts you, choose Yes.
  echo.
  set "FORCE_ARG="
  if "%FORCE_FRESH%"=="1" set "FORCE_ARG=,'--force'"
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WindowStyle Normal -ArgumentList '--elevated','%PF_USER%'%FORCE_ARG%" >nul 2>&1
  if errorlevel 1 (
    echo Failed to request administrator elevation.
    echo.
    pause
    exit /b 1
  )
  rem Elevation succeeded - close this window, elevated instance will show result
  exit /b 0
)

set "RC=0"
set "FAIL_REASON="
set "FRESH_INSTALL=0"
set "WAS_REINSTALL=0"

rem Detect fresh install (directory didn't exist before)
if not exist "%INSTALLDIR%" (
  set "FRESH_INSTALL=1"
  mkdir "%INSTALLDIR%" >nul 2>&1
) else (
  set "WAS_REINSTALL=1"
  rem --force flag treats reinstall as fresh install
  if "%FORCE_FRESH%"=="1" set "FRESH_INSTALL=1"
)
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1

(
  echo.
  echo =======================
  echo %date% %time% Install started
  echo InstallDir: %INSTALLDIR%
  echo SourceDir : %SRCDIR%
  echo UserId    : %PF_USER%  if "%WAS_REINSTALL%"=="1" (
    echo InstallType: UPDATE/REINSTALL (directory already exists)
    if "%FORCE_FRESH%"=="1" (
      echo ForceFlag : YES (--force flag set, will treat as fresh install)
    ) else (
      echo ForceFlag : NO (will preserve config, skip auto-open)
    )
  ) else (
    echo InstallType: FRESH INSTALL (new installation)
  )  echo =======================
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

rem Copy icon (required for tray mode)
if exist "%SRCDIR%portflow.ico" (
  copy /y "%SRCDIR%portflow.ico" "%INSTALLDIR%\portflow.ico" >nul
)

rem Copy docs and tooling (overwrite so users keep latest instructions)
if exist "%SRCDIR%README-SETUP.txt" copy /y "%SRCDIR%README-SETUP.txt" "%INSTALLDIR%\README-SETUP.txt" >nul
if exist "%SRCDIR%README-TROUBLESHOOTING.txt" copy /y "%SRCDIR%README-TROUBLESHOOTING.txt" "%INSTALLDIR%\README-TROUBLESHOOTING.txt" >nul
if exist "%SRCDIR%install.cmd" copy /y "%SRCDIR%install.cmd" "%INSTALLDIR%\install.cmd" >nul
if exist "%SRCDIR%uninstall.cmd" copy /y "%SRCDIR%uninstall.cmd" "%INSTALLDIR%\uninstall.cmd" >nul
if exist "%SRCDIR%install-task.ps1" copy /y "%SRCDIR%install-task.ps1" "%INSTALLDIR%\install-task.ps1" >nul
if exist "%SRCDIR%test-config.cmd" copy /y "%SRCDIR%test-config.cmd" "%INSTALLDIR%\test-config.cmd" >nul

if exist "%SRCDIR%PORTFLOW_TARGET.txt" (
  copy /y "%SRCDIR%PORTFLOW_TARGET.txt" "%INSTALLDIR%\PORTFLOW_TARGET.txt" >nul
)

rem Preserve user-editable config across reinstalls
if not exist "%INSTALLDIR%\portflow.backup.json" (
  (
    echo %date% %time% Installing new config file (portflow.backup.json)
  )>>"%%INSTALLLOG%%" 2>nul
  if not exist "%SRCDIR%portflow.backup.json" (
    set "RC=1"
    set "FAIL_REASON=portflow.backup.json not found in source folder"
    goto done
  )
  copy "%SRCDIR%portflow.backup.json" "%INSTALLDIR%\portflow.backup.json" >nul
  if not "%%errorlevel%%"=="0" (
    set "RC=1"
    set "FAIL_REASON=Failed to copy portflow.backup.json"
    goto done
  )
) else (
  (
    echo %date% %time% Preserving existing config file (portflow.backup.json not overwritten)
  )>>"%%INSTALLLOG%%" 2>nul
)

rem Register / replace scheduled task (use installed copy for reliability)
powershell -NoProfile -ExecutionPolicy Bypass -File "%INSTALLDIR%\install-task.ps1" -InstallDir "%INSTALLDIR%" -UserId "%PF_USER%" >>%INSTALLLOG% 2>&1
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
    if "%WAS_REINSTALL%"=="1" (
      echo %date% %time% Install result: SUCCESS (UPDATE)
      echo %date% %time% Existing installation was updated with latest files
    ) else (
      echo %date% %time% Install result: SUCCESS (FRESH INSTALL)
      echo %date% %time% New installation completed
    )
    if "%FRESH_INSTALL%"=="1" (
      echo %date% %time% Auto-opening: Explorer + README + config (fresh install behavior)
    ) else (
      echo %date% %time% Auto-opening: SKIPPED (reinstall, user files preserved)
    )
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
  if "%WAS_REINSTALL%"=="1" (
    echo Update complete.
    echo PortFlowBackup was already installed and has been updated.
    echo Installed to: %INSTALLDIR%\
    echo Scheduled task: PortFlowBackup
    echo.
    echo Your existing configuration (portflow.backup.json) was preserved.
    if "%FORCE_FRESH%"=="1" (
      echo.
      echo (--force flag: Opening files as if this were a fresh install)
    )
  ) else (
    echo Install complete.
    echo Installed to: %INSTALLDIR%\
    echo Scheduled task: PortFlowBackup
  )
  
  rem Only auto-open files on fresh install (avoid noise on reinstall/update)
  if "%FRESH_INSTALL%"=="1" (
    echo.
    echo Opening installation folder and configuration files...
    
    rem Open installation directory in File Explorer
    start "" explorer "%INSTALLDIR%"
    
    rem Open setup instructions
    if exist "%INSTALLDIR%\README-SETUP.txt" (
      start "" notepad "%INSTALLDIR%\README-SETUP.txt"
    )
    
    rem Open config file for editing
    if exist "%INSTALLDIR%\portflow.backup.json" (
      start "" notepad "%INSTALLDIR%\portflow.backup.json"
    )
  )
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
