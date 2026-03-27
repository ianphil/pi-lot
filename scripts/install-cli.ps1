<#
.SYNOPSIS
    Installs the llm CLI to your user PATH.
.DESCRIPTION
    Publishes the llm-cli project as a self-contained executable to
    %LOCALAPPDATA%\Programs\llm and adds it to the user PATH (persisted
    via registry). After install, "llm ask ..." works from any terminal.
.PARAMETER InstallPath
    Where to publish the binary. Default: $env:LOCALAPPDATA\Programs\llm
#>
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\llm"
)

$ProjectDir = Split-Path $PSScriptRoot -Parent
$CliProject = Join-Path $ProjectDir "llm-cli\llm-cli.csproj"
$ErrorActionPreference = "Stop"

if (-not (Test-Path $CliProject)) {
    Write-Error "Could not find $CliProject"
    exit 1
}

# ── Publish ──────────────────────────────────────────────────────────────────
Write-Host "Publishing llm CLI to $InstallPath..." -ForegroundColor Cyan
dotnet publish $CliProject -c Release -o $InstallPath --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}
Write-Host "Published successfully." -ForegroundColor Green

# ── Add to user PATH ─────────────────────────────────────────────────────────
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -split ";" | Where-Object { $_ -eq $InstallPath }) {
    Write-Host "PATH already contains $InstallPath" -ForegroundColor DarkGray
} else {
    Write-Host "Adding $InstallPath to user PATH..." -ForegroundColor Cyan
    $newPath = if ($userPath) { "$userPath;$InstallPath" } else { $InstallPath }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    # Also update current session so it works immediately
    $env:Path += ";$InstallPath"
    Write-Host "Added to PATH (persisted via registry)." -ForegroundColor Green
}

# ── Verify ───────────────────────────────────────────────────────────────────
$exe = Join-Path $InstallPath "llm.exe"
if (Test-Path $exe) {
    $version = & $exe --version 2>&1
    Write-Host ""
    Write-Host "llm CLI installed!" -ForegroundColor Green
    Write-Host "  Location: $exe" -ForegroundColor White
    Write-Host "  Version:  $version" -ForegroundColor White
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Cyan
    Write-Host "  llm ask `"your prompt`"" -ForegroundColor White
    Write-Host "  llm models" -ForegroundColor White
    Write-Host "  llm health" -ForegroundColor White
    Write-Host ""
    Write-Host "Open a new terminal if 'llm' is not found in your current session." -ForegroundColor DarkGray
} else {
    Write-Warning "Published but llm.exe not found at $exe"
}
