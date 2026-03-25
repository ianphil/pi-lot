#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the Copilot LLM Proxy Windows service.
.PARAMETER RemoveBinaries
    If set, also deletes the published binaries.
.PARAMETER InstallPath
    Where the service binaries are. Default: C:\services\llm-svc
#>
param(
    [switch]$RemoveBinaries,
    [string]$InstallPath = "C:\services\llm-svc"
)

$ServiceName = "CopilotLlmProxy"
$ErrorActionPreference = "Stop"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' not found. Nothing to do." -ForegroundColor Yellow
    exit 0
}

if ($existing.Status -eq "Running") {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Removing service..." -ForegroundColor Yellow
sc.exe delete $ServiceName | Out-Null
Write-Host "Service removed." -ForegroundColor Green

# ── Remove Event Log source ──────────────────────────────────────────────────
if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    Write-Host "Removing Event Log source..." -ForegroundColor Yellow
    Remove-EventLog -Source $ServiceName
    Write-Host "Event Log source removed." -ForegroundColor Green
}

if ($RemoveBinaries -and (Test-Path $InstallPath)) {
    Write-Host "Removing binaries at $InstallPath..." -ForegroundColor Yellow
    Remove-Item $InstallPath -Recurse -Force
    Write-Host "Binaries removed." -ForegroundColor Green
}
