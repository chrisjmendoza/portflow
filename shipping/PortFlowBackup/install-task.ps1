# PortFlow USB Backup - Scheduled Task Installer
# Must be run as Administrator

$ErrorActionPreference = "Stop"

$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $baseDir "PortFlow.Runner.exe"
$configPath = Join-Path $baseDir "portflow.backup.json"

if (!(Test-Path $exePath)) {
    throw "PortFlow.Runner.exe not found in $baseDir"
}

if (!(Test-Path $configPath)) {
    throw "portflow.backup.json not found in $baseDir"
}

$taskName = "PortFlow USB Backup"

$action = New-ScheduledTaskAction `
    -Execute $exePath `
    -Argument "--config `"$configPath`""

$trigger = New-ScheduledTaskTrigger -AtLogOn

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -Hidden `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Run in user context (no password prompt)
$principal = New-ScheduledTaskPrincipal `
    -UserId "$env:USERNAME" `
    -LogonType Interactive `
    -RunLevel Highest

# Remove existing task if present
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal

Write-Host "PortFlow USB Backup installed successfully."
