# PortFlow Configuration Validator
# Tests portflow.backup.json for common errors before running backups

param(
    [Parameter(Mandatory=$false)]
    [string]$ConfigPath = ".\portflow.backup.json"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "PortFlow Configuration Validator" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if config file exists
if (-not (Test-Path $ConfigPath)) {
    Write-Host "ERROR: Configuration file not found: $ConfigPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Expected location: C:\ProgramData\PortFlowBackup\portflow.backup.json" -ForegroundColor Yellow
    exit 1
}

Write-Host "Step 1: Checking JSON syntax..." -ForegroundColor White
try {
    $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    Write-Host "  OK - Valid JSON" -ForegroundColor Green
} catch {
    Write-Host "  FAILED - Invalid JSON syntax" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error details:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Common JSON errors:" -ForegroundColor Yellow
    Write-Host "  - Missing or extra commas" -ForegroundColor Gray
    Write-Host "  - Unmatched quotes or brackets" -ForegroundColor Gray
    Write-Host "  - Single backslashes (use \\ instead of \)" -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "Step 2: Validating required fields..." -ForegroundColor White

$errors = @()

# Check sourcePath
if (-not $config.sourcePath) {
    $errors += "  - Missing 'sourcePath' field"
} elseif (-not (Test-Path $config.sourcePath)) {
    $errors += "  - Source path does not exist: $($config.sourcePath)"
} else {
    Write-Host "  OK - sourcePath: $($config.sourcePath)" -ForegroundColor Green
}

# Check destinationFolderName
if ($null -eq $config.destinationFolderName) {
    $errors += "  - Missing 'destinationFolderName' field"
} else {
    Write-Host "  OK - destinationFolderName: '$($config.destinationFolderName)'" -ForegroundColor Green
    if ($config.destinationFolderName -eq "") {
        Write-Host "     (Files will be copied to USB root)" -ForegroundColor Gray
    }
}

# Check mirror
if ($null -eq $config.mirror) {
    $errors += "  - Missing 'mirror' field (should be true or false)"
} else {
    Write-Host "  OK - mirror: $($config.mirror)" -ForegroundColor Green
    if ($config.mirror -eq $true) {
        Write-Host "     WARNING: Mirror mode enabled - files not in source will be DELETED from backup" -ForegroundColor Yellow
    }
}

# Check logPath
if (-not $config.logPath) {
    $errors += "  - Missing 'logPath' field"
} else {
    $logDir = Split-Path $config.logPath -Parent
    if ($logDir -and -not (Test-Path $logDir)) {
        try {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            Write-Host "  OK - logPath: $($config.logPath) (directory created)" -ForegroundColor Green
        } catch {
            $errors += "  - Cannot create log directory: $logDir"
        }
    } else {
        Write-Host "  OK - logPath: $($config.logPath)" -ForegroundColor Green
    }
}

# Check stayRunning
if ($null -eq $config.stayRunning) {
    $errors += "  - Missing 'stayRunning' field (should be true or false)"
} else {
    Write-Host "  OK - stayRunning: $($config.stayRunning)" -ForegroundColor Green
}

Write-Host ""

# Report errors
if ($errors.Count -gt 0) {
    Write-Host "Validation FAILED with errors:" -ForegroundColor Red
    Write-Host ""
    foreach ($error in $errors) {
        Write-Host $error -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}

Write-Host "Step 3: Checking backup size estimate..." -ForegroundColor White
try {
    $sourceSize = (Get-ChildItem $config.sourcePath -Recurse -File -ErrorAction SilentlyContinue | 
                   Measure-Object -Property Length -Sum).Sum
    
    if ($sourceSize) {
        $sizeGB = [math]::Round($sourceSize / 1GB, 2)
        $sizeMB = [math]::Round($sourceSize / 1MB, 2)
        
        if ($sizeGB -gt 1) {
            Write-Host "  Source folder size: $sizeGB GB" -ForegroundColor Green
        } else {
            Write-Host "  Source folder size: $sizeMB MB" -ForegroundColor Green
        }
        
        Write-Host "  Make sure your USB drive has at least this much free space." -ForegroundColor Gray
    } else {
        Write-Host "  Source folder appears empty or inaccessible" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Could not calculate size (permissions or access issue)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Configuration is VALID" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Ensure PORTFLOW_TARGET.txt is on your USB drive root" -ForegroundColor Gray
Write-Host "  2. Plug in the USB drive" -ForegroundColor Gray
Write-Host "  3. PortFlow will automatically run the backup" -ForegroundColor Gray
Write-Host ""

exit 0
