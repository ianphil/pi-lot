#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Copilot LLM Proxy as a Scheduled Task (alternative to Windows service).
.DESCRIPTION
    On domain-joined machines, "Log on as a service" is often blocked by Group Policy.
    A scheduled task with "Run at logon" avoids this restriction while still starting
    the proxy automatically.
.PARAMETER InstallPath
    Where to publish the binaries. Default: C:\services\llm-svc
#>
param(
    [string]$InstallPath = "C:\services\llm-svc"
)

$TaskName = "CopilotLlmProxy"
$ProjectDir = Split-Path $PSScriptRoot -Parent
$ErrorActionPreference = "Stop"

# ── Remove existing task if present ──────────────────────────────────────────
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing scheduled task..." -ForegroundColor Yellow
    if ($existing.State -eq "Running") {
        Stop-ScheduledTask -TaskName $TaskName
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# ── Publish ──────────────────────────────────────────────────────────────────
Write-Host "Publishing to $InstallPath..." -ForegroundColor Cyan
dotnet publish $ProjectDir -c Release -o $InstallPath --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}
Write-Host "Published successfully." -ForegroundColor Green

# ── Register Event Log source ────────────────────────────────────────────────
if (-not [System.Diagnostics.EventLog]::SourceExists($TaskName)) {
    Write-Host "Registering Event Log source '$TaskName'..." -ForegroundColor Cyan
    New-EventLog -LogName $TaskName -Source $TaskName
    Write-Host "Event Log source registered." -ForegroundColor Green
} else {
    Write-Host "Event Log source '$TaskName' already exists." -ForegroundColor DarkGray
}

# ── Create scheduled task ────────────────────────────────────────────────────
Write-Host "Creating scheduled task '$TaskName'..." -ForegroundColor Cyan

$action = New-ScheduledTaskAction `
    -Execute "$InstallPath\llm-svc.exe" `
    -WorkingDirectory $InstallPath

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Local OpenAI-compatible proxy to GitHub Copilot LLM API (localhost:5100)" | Out-Null

# ── Start it now ─────────────────────────────────────────────────────────────
Write-Host "Starting task..." -ForegroundColor Cyan
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 3

$task = Get-ScheduledTask -TaskName $TaskName
if ($task.State -eq "Running") {
    Write-Host ""
    Write-Host "Copilot LLM Proxy is running!" -ForegroundColor Green
    Write-Host "  Endpoint: http://localhost:5100/v1" -ForegroundColor White
    Write-Host "  Health:   http://localhost:5100/health" -ForegroundColor White
    Write-Host ""
    Write-Host "The proxy starts automatically at logon." -ForegroundColor Cyan
    Write-Host "Manage with:" -ForegroundColor DarkGray
    Write-Host "  Stop:     Stop-ScheduledTask -TaskName $TaskName" -ForegroundColor DarkGray
    Write-Host "  Start:    Start-ScheduledTask -TaskName $TaskName" -ForegroundColor DarkGray
    Write-Host "  Remove:   .\scripts\uninstall.ps1" -ForegroundColor DarkGray
} else {
    Write-Host "Task registered but may not be running. Check with:" -ForegroundColor Yellow
    Write-Host "  Get-ScheduledTask -TaskName $TaskName | Format-List State" -ForegroundColor White
}
