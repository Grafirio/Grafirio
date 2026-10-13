<#
.SYNOPSIS
  Registers (or removes) the Grafirio Ops agent as a scheduled task that starts at logon.

.DESCRIPTION
  A scheduled task is used instead of a Windows service on purpose: starting Docker Desktop
  requires an interactive user session, which a service account does not have.
  Run from a normal (non-elevated) PowerShell as the user who owns Docker Desktop.
#>
param(
    [string]$TaskName = 'Grafirio Ops Agent',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed scheduled task '$TaskName'." -ForegroundColor Green
    return
}

$agentDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..\src\front\admins\grifirio.projectadmin\api')).Path
$nodePath = (Get-Command node -ErrorAction Stop).Source

Push-Location $agentDirectory
try {
    npm.cmd ci --omit=dev
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
} finally {
    Pop-Location
}

$action = New-ScheduledTaskAction -Execute $nodePath `
    -Argument '--env-file-if-exists=../../../../../.env --env-file-if-exists=.env server.js' `
    -WorkingDirectory $agentDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Monitors and controls the Grafirio infrastructure (databases, Docker, services).' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Scheduled task '$TaskName' registered and started." -ForegroundColor Green
Write-Host 'Recovery screen: http://127.0.0.1:3001/recovery/ (needs OPS_RECOVERY_KEY in .env)' -ForegroundColor Cyan
