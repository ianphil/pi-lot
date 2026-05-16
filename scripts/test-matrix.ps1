<#
.SYNOPSIS
    Runs llm CLI smoke commands against llm-svc.
.DESCRIPTION
    Each test-matrix row maps to a user-visible llm command against a live
    proxy. Service routing and translation correctness belongs in llm-svc.Int.
    Starts llm-svc automatically if the selected endpoint is not already
    healthy; reuses an already-running proxy otherwise.
.NOTES
    Models used:
      gpt-5.4-mini -> responses smoke
      gpt-5-mini   -> chat smoke
    Run:  pwsh scripts\test-matrix.ps1 [-Port 5110] [-NoStream]
    Options:
      -Port <port>       Port to use when -Endpoint is not supplied.
      -HostName <host>   Host to bind when -Endpoint is not supplied.
      -Endpoint <url>    Full proxy endpoint override.
      -UseEnvToken       Leave COPILOT_TOKEN unchanged instead of unsetting it.
      -Dotnet <path>     Path to dotnet executable.
#>

param(
    [string]$Endpoint,
    [string]$HostName = "127.0.0.1",
    [int]$Port = 5100,
    [switch]$NoStream,
    [switch]$UseEnvToken,
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"

# Resolve the llm-cli project relative to this script
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$llmCli   = Join-Path $repoRoot "src\llm-cli"
$serviceProject = Join-Path $repoRoot "src\llm-svc\llm-svc.csproj"

if (-not $PSBoundParameters.ContainsKey("Endpoint")) {
    $Endpoint = "http://${HostName}:$Port"
}

Write-Host "Building llm-cli..." -ForegroundColor Yellow
& $Dotnet build $llmCli --no-restore --disable-build-servers -m:1 --no-incremental
Write-Host "Build complete." -ForegroundColor Yellow

$serviceProcess = $null
$startedService = $false
$stdoutLog = Join-Path ([System.IO.Path]::GetTempPath()) "llm-svc-test-matrix-$PID.out.log"
$stderrLog = Join-Path ([System.IO.Path]::GetTempPath()) "llm-svc-test-matrix-$PID.err.log"

function Test-ProxyHealth {
    try {
        Invoke-WebRequest -Uri "$Endpoint/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Get-ServiceLog {
    $parts = @()
    if (Test-Path $stdoutLog) {
        $parts += "stdout:`n$(Get-Content $stdoutLog -Raw)"
    }
    if (Test-Path $stderrLog) {
        $parts += "stderr:`n$(Get-Content $stderrLog -Raw)"
    }

    return $parts -join "`n"
}

function Start-ProxyIfNeeded {
    if (Test-ProxyHealth) {
        Write-Host "Proxy already healthy at $Endpoint - reusing." -ForegroundColor Yellow
        return
    }

    Write-Host "Starting llm-svc at $Endpoint..." -ForegroundColor Yellow

    Remove-Item $stdoutLog, $stderrLog -ErrorAction SilentlyContinue

    $previousToken = $env:COPILOT_TOKEN
    $hadToken = Test-Path Env:COPILOT_TOKEN

    if (-not $UseEnvToken) {
        Remove-Item Env:COPILOT_TOKEN -ErrorAction SilentlyContinue
    }

    try {
        $script:serviceProcess = Start-Process $Dotnet `
            -ArgumentList @("run", "--no-launch-profile", "--project", $serviceProject) `
            -WorkingDirectory $repoRoot `
            -Environment @{ Kestrel__Endpoints__Http__Url = $Endpoint } `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog `
            -PassThru
    }
    finally {
        if ($hadToken) {
            $env:COPILOT_TOKEN = $previousToken
        }
    }

    $script:startedService = $true

    for ($i = 0; $i -lt 60; $i++) {
        if ($serviceProcess.HasExited) {
            throw "llm-svc exited before becoming ready.`n$(Get-ServiceLog)"
        }

        if (Test-ProxyHealth) {
            Write-Host "llm-svc ready (pid $($serviceProcess.Id))." -ForegroundColor Green
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "llm-svc did not become ready at $Endpoint.`n$(Get-ServiceLog)"
}

function Stop-StartedProxy {
    if ($startedService -and $null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Write-Host ""
        Write-Host "Stopping llm-svc (pid $($serviceProcess.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $serviceProcess.Id
        $serviceProcess.WaitForExit(10000) | Out-Null
    }

    Remove-Item $stdoutLog, $stderrLog -ErrorAction SilentlyContinue
}

function Invoke-Llm {
    param(
        [string]$Label,
        [string]$Verb,
        [string]$Prompt,
        [string]$Model,
        [switch]$Stream,
        [switch]$Tools,
        [string[]]$ExtraArgs = @()
    )

    $cliArgs = @($Verb, $Prompt, "-m", $Model, "-e", $Endpoint)
    if (-not $Stream) { $cliArgs += "--no-stream" }
    if ($Tools)       { $cliArgs += "--tools" }
    if ($ExtraArgs.Count -gt 0) { $cliArgs += $ExtraArgs }

    Write-Host ""
    Write-Host "─── $Label ───" -ForegroundColor Cyan
    Write-Host "  llm $($cliArgs -join ' ')" -ForegroundColor DarkGray

    try {
        $output = & $Dotnet run --project $llmCli --no-build -- @cliArgs 2>&1
        $text = ($output | Out-String).Trim()
        if ($text -match "Unhandled exception|FAIL|error") {
            if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + "..." }
            Write-Host "  FAIL: $text" -ForegroundColor Red
            return $false
        }
        if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + "..." }
        Write-Host "  OK: $text" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "  FAIL: $_" -ForegroundColor Red
        return $false
    }
}

function Invoke-LlmCommand {
    param(
        [string]$Label,
        [string[]]$CliArgs
    )

    Write-Host ""
    Write-Host "─── $Label ───" -ForegroundColor Cyan
    Write-Host "  llm $($CliArgs -join ' ')" -ForegroundColor DarkGray

    try {
        $output = & $Dotnet run --project $llmCli --no-build -- @CliArgs 2>&1
        $text = ($output | Out-String).Trim()
        if ($text -match "Unhandled exception|FAIL|error") {
            if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + "..." }
            Write-Host "  FAIL: $text" -ForegroundColor Red
            return $false
        }
        if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + "..." }
        Write-Host "  OK: $text" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "  FAIL: $_" -ForegroundColor Red
        return $false
    }
}

trap {
    Stop-StartedProxy
    throw
}

Start-ProxyIfNeeded

$useStream = -not $NoStream
$pass = 0
$fail = 0

$prompt      = "Reply with exactly: hello"
$toolPrompt  = "Use the fetch_url tool to fetch https://raw.githubusercontent.com/github/gitignore/main/README.md and summarize it in one sentence"

if (Invoke-LlmCommand "1. health → CLI can reach proxy" @("health", "-e", $Endpoint)) { $pass++ } else { $fail++ }

if (Invoke-Llm "2. ask → responses smoke" ask $prompt gpt-5.4-mini -Stream:$false) { $pass++ } else { $fail++ }

if (Invoke-Llm "3. chat → chat smoke" chat $prompt gpt-5-mini -Stream:$useStream) { $pass++ } else { $fail++ }

if (Invoke-Llm "4. ask → local tools smoke" ask $toolPrompt gpt-5-mini -Stream:$false -Tools) { $pass++ } else { $fail++ }

if (Invoke-Llm "5. ask → CLI per-call knobs smoke" ask $prompt gpt-5.4-mini -Stream:$false -ExtraArgs @(
    "--request-id", "test-matrix-cli-ask",
    "--correlation-id", "test-matrix-cli-correlation",
    "--metadata", "surface=ask",
    "--timeout-ms", "60000",
    "--max-retries", "1",
    "--max-retry-delay-ms", "1000"
)) { $pass++ } else { $fail++ }

# ══════════════════════════════════════════════════════════════════════════════
# Summary
# ══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor White
Write-Host "  Results: $pass passed, $fail failed  (of $($pass + $fail))" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
Write-Host "═══════════════════════════════════════" -ForegroundColor White

Stop-StartedProxy

exit $fail
