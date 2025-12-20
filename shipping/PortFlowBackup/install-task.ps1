# PortFlowBackup - Scheduled Task Installer (internal helper)
# Must be run as Administrator

param(
    [Parameter(Mandatory = $false)]
    [string]$InstallDir = "C:\ProgramData\PortFlowBackup",

    [Parameter(Mandatory = $false)]
    [string]$UserId = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $InstallDir "PortFlow.Runner.exe"
 $configPath = Join-Path $InstallDir "portflow.backup.json"

if (!(Test-Path -LiteralPath $exePath)) {
    throw "PortFlow.Runner.exe not found at $exePath"
}

if (!(Test-Path -LiteralPath $configPath)) {
    throw "portflow.backup.json not found at $configPath"
}

$taskName = "PortFlowBackup"

$action = New-ScheduledTaskAction `
    -Execute $exePath `
    -Argument "--config `"$configPath`"" `
    -WorkingDirectory $InstallDir

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -Hidden `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Run only when the user is logged on (interactive context for user-profile paths)
$principal = New-ScheduledTaskPrincipal `
    -UserId $UserId `
    -LogonType InteractiveToken `
    -RunLevel Highest

$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal

Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
