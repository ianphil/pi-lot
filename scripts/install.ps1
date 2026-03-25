#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes and installs the Copilot LLM Proxy as a Windows service.
.DESCRIPTION
    Builds a self-contained publish, registers it with sc.exe, and
    reminds you to set the Log On account via services.msc so the
    service can access your Copilot CLI credential.
.PARAMETER InstallPath
    Where to publish the service binaries. Default: C:\services\llm-svc
#>
param(
    [string]$InstallPath = "C:\services\llm-svc"
)

$ServiceName = "CopilotLlmProxy"
$DisplayName = "Copilot LLM Proxy"
$ProjectDir  = Split-Path $PSScriptRoot -Parent
$ErrorActionPreference = "Stop"

# ── Stop and remove existing service if present ──────────────────────────────
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    if ($existing.Status -eq "Running") {
        sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    Write-Host "Removing existing service..." -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# ── Publish ──────────────────────────────────────────────────────────────────
Write-Host "Publishing to $InstallPath..." -ForegroundColor Cyan
dotnet publish $ProjectDir -c Release -o $InstallPath --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}
Write-Host "Published successfully." -ForegroundColor Green

# ── Install service ──────────────────────────────────────────────────────────
Write-Host "Creating Windows service '$DisplayName'..." -ForegroundColor Cyan
sc.exe create $ServiceName `
    binPath="$InstallPath\llm-svc.exe" `
    displayname="$DisplayName" `
    start=auto
if ($LASTEXITCODE -ne 0) {
    Write-Error "sc.exe create failed."
    exit 1
}

# Set a description in services.msc
sc.exe description $ServiceName "Local OpenAI-compatible proxy to GitHub Copilot LLM API (localhost:5100)"

Write-Host ""
Write-Host "Service installed." -ForegroundColor Green
Write-Host ""
Write-Host "NEXT STEP: Set the Log On account so the service can access your credentials:" -ForegroundColor Yellow
Write-Host "  1. Open services.msc" -ForegroundColor White
Write-Host "  2. Find '$DisplayName'" -ForegroundColor White
Write-Host "  3. Right-click -> Properties -> Log On tab" -ForegroundColor White
Write-Host "  4. Select 'This account' and enter your Windows credentials" -ForegroundColor White
Write-Host "  5. Click OK, then Start the service" -ForegroundColor White
Write-Host ""
Write-Host "Or start now (runs as Local System — won't see your credential store):" -ForegroundColor DarkGray
Write-Host "  sc.exe start $ServiceName" -ForegroundColor DarkGray
