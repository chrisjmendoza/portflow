@echo off
setlocal

rem PortFlowBackup shipping uninstaller
rem Removes scheduled task and deletes C:\ProgramData\PortFlowBackup.

set "INSTALLDIR=C:\ProgramData\PortFlowBackup"

set "TASKNAME=PortFlowBackup"
set "INSTALLDIR_BS=%INSTALLDIR%\"
set "SCRIPTDIR=%~dp0"
set "SELF=%~f0"
set "TEMPCOPY=%TEMP%\PortFlowBackup.uninstall.%RANDOM%.cmd"

if /i "%~1"=="--cleanup" goto cleanup

echo.
echo PortFlowBackup Uninstaller
echo ==========================
echo.

rem Ensure admin elevation
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Administrator access is required to uninstall.
  echo If Windows prompts you, choose Yes.
  echo.
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -Verb RunAs -WindowStyle Normal -ArgumentList '/c','\"\"%SELF%\"\" --cleanup'" >nul 2>&1
  echo A new uninstaller window should open.
  echo.
  exit /b 0
)

rem If the user runs uninstall from inside %INSTALLDIR%, this script will be locked.
rem Run a temporary copy from %TEMP% so we can delete %INSTALLDIR%.
if /i "%SCRIPTDIR%"=="%INSTALLDIR_BS%" (
  copy /y "%SELF%" "%TEMPCOPY%" >nul 2>&1
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -WindowStyle Normal -ArgumentList '/c','\"\"%TEMPCOPY%\"\" --cleanup'" >nul 2>&1
  echo Uninstall is running from a separate window.
  echo You may close this window now.
  echo.
  exit /b 0
)

goto cleanup

:cleanup
echo Removing PortFlowBackup...
echo Target folder:
echo   %INSTALLDIR%\
echo.

rem Stop any running instance so files can be removed.
schtasks /End /TN "%TASKNAME%" >nul 2>&1
taskkill /F /IM "PortFlow.Runner.exe" /T >nul 2>&1

rem Remove scheduled task (ignore if missing)
rem Use explicit full path: \PortFlowBackup\PortFlowBackup
set "TASK_DELETE_OK=0"
schtasks /Delete /TN "%TASKNAME%" /F >nul 2>&1 && set "TASK_DELETE_OK=1"

rem Fallback: attempt removal via PowerShell ScheduledTasks module.
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $t = Get-ScheduledTask -TaskPath '\PortFlowBackup\' -TaskName 'PortFlowBackup' -ErrorAction SilentlyContinue; if ($t) { Unregister-ScheduledTask -TaskPath '\PortFlowBackup\' -TaskName 'PortFlowBackup' -Confirm:$false -ErrorAction SilentlyContinue | Out-Null; exit 0 } else { exit 0 } } catch { exit 0 }" >nul 2>&1

rem Re-check whether task still exists.
schtasks /Query /TN "%TASKNAME%" >nul 2>&1 && set "TASK_DELETE_OK=0"

rem Remove installed files (retry a few times in case the process is exiting)
set "RC=0"
for /l %%i in (1,1,5) do (
  rmdir /s /q "%INSTALLDIR%" >nul 2>&1
  if not exist "%INSTALLDIR%" goto removed
  timeout /t 1 /nobreak >nul
)

:removed
if exist "%INSTALLDIR%" (
  set "RC=1"
  echo WARNING: Some files could not be removed.
  echo Please close any PortFlow windows and try again.
) else (
  echo Uninstall complete.
  if "%TASK_DELETE_OK%"=="1" (
    echo Scheduled task "%TASKNAME%" removed.
  ) else (
    echo WARNING: Scheduled task "%TASKNAME%" may still exist.
    echo If so, open Task Scheduler and delete it manually.
  )
)

echo.
pause

rem If this is a temp copy, clean it up.
if /i "%~dp0"=="%TEMP%\" del /f /q "%~f0" >nul 2>&1

exit /b %RC%
