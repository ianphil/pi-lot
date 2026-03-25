#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the Copilot LLM Proxy scheduled task.
.PARAMETER RemoveBinaries
    If set, also deletes the published binaries.
.PARAMETER InstallPath
    Where the binaries are. Default: C:\services\llm-svc
#>
param(
    [switch]$RemoveBinaries,
    [string]$InstallPath = "C:\services\llm-svc"
)

$TaskName = "CopilotLlmProxy"
$ErrorActionPreference = "Stop"

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Scheduled task '$TaskName' not found. Nothing to do." -ForegroundColor Yellow
    exit 0
}

if ($existing.State -eq "Running") {
    Write-Host "Stopping task..." -ForegroundColor Yellow
    Stop-ScheduledTask -TaskName $TaskName
}

Write-Host "Removing scheduled task..." -ForegroundColor Yellow
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Task removed." -ForegroundColor Green

if ($RemoveBinaries -and (Test-Path $InstallPath)) {
    Write-Host "Removing binaries at $InstallPath..." -ForegroundColor Yellow
    Remove-Item $InstallPath -Recurse -Force
    Write-Host "Binaries removed." -ForegroundColor Green
}
