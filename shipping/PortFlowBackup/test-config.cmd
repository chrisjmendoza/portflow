@echo off
rem PortFlow Configuration Validator
rem Tests portflow.backup.json for common configuration errors

setlocal

set "INSTALLDIR=C:\ProgramData\PortFlowBackup"
set "CONFIGFILE=%INSTALLDIR%\portflow.backup.json"

rem Check if running from install directory or shipping folder
if exist ".\portflow.backup.json" (
    set "CONFIGFILE=.\portflow.backup.json"
)

echo.
echo PortFlow Configuration Validator
echo ================================
echo.

if not exist "%CONFIGFILE%" (
    echo ERROR: Configuration file not found: %CONFIGFILE%
    echo.
    echo Expected location: C:\ProgramData\PortFlowBackup\portflow.backup.json
    echo.
    pause
    exit /b 1
)

echo Testing configuration: %CONFIGFILE%
echo.

rem Run PowerShell validation script
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $config = '%CONFIGFILE%'; & { param($c) try { $j = Get-Content $c -Raw | ConvertFrom-Json; Write-Host 'JSON Syntax: OK' -ForegroundColor Green; if (-not $j.sourcePath) { Write-Host 'ERROR: Missing sourcePath' -ForegroundColor Red; exit 1 }; if (-not (Test-Path $j.sourcePath)) { Write-Host 'ERROR: Source path does not exist: ' $j.sourcePath -ForegroundColor Red; exit 1 }; Write-Host 'Source Path: ' $j.sourcePath -ForegroundColor Green; Write-Host 'Destination: ' $j.destinationFolderName -ForegroundColor Green; Write-Host 'Mirror Mode: ' $j.mirror -ForegroundColor Green; if ($j.mirror -eq $true) { Write-Host 'WARNING: Mirror mode will DELETE files from backup that are not in source' -ForegroundColor Yellow }; Write-Host ''; Write-Host 'Configuration is VALID' -ForegroundColor Green; Write-Host ''; Write-Host 'Next steps:' -ForegroundColor White; Write-Host '  1. Ensure PORTFLOW_TARGET.txt is on your USB drive root' -ForegroundColor Gray; Write-Host '  2. Plug in the USB drive' -ForegroundColor Gray; Write-Host '  3. PortFlow will run backup automatically' -ForegroundColor Gray; exit 0 } catch { Write-Host 'ERROR: Invalid JSON syntax' -ForegroundColor Red; Write-Host ''; Write-Host 'Details: ' $_.Exception.Message -ForegroundColor Yellow; Write-Host ''; Write-Host 'Common JSON errors:' -ForegroundColor Yellow; Write-Host '  - Missing or extra commas' -ForegroundColor Gray; Write-Host '  - Unmatched quotes or brackets' -ForegroundColor Gray; Write-Host '  - Single backslashes (use \\ instead of \)' -ForegroundColor Gray; exit 1 } } $config }"

if not "%errorlevel%"=="0" (
    echo.
    echo Configuration test FAILED.
    echo Please fix the errors above and try again.
    echo.
    pause
    exit /b 1
)

echo.
pause
exit /b 0
