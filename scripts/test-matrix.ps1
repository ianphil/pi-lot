<#
.SYNOPSIS
    Runs llm CLI commands matching every scenario in the test matrix.
.DESCRIPTION
    Each test-matrix row maps to an llm ask, llm chat, llm sdk-ask,
    llm sdk-context-ask, or llm sdk-chat invocation that exercises the same
    surface or SDK path.
    Starts llm-svc automatically if the selected endpoint is not already
    healthy; reuses an already-running proxy otherwise.
.NOTES
    Models used:
      gpt-5.4-mini     -> /responses only
      gpt-5.4          -> /responses only
      claude-haiku-4.5 -> /chat/completions only
      gpt-5-mini       -> both /chat/completions and /responses (dual)
      sdk-* commands   -> direct ILlmSdkClient path
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
        [bool]$UseEndpoint = $true,
        [string[]]$ExtraArgs = @()
    )

    $cliArgs = @($Verb, $Prompt, "-m", $Model)
    if ($UseEndpoint) { $cliArgs += @("-e", $Endpoint) }
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

trap {
    Stop-StartedProxy
    throw
}

Start-ProxyIfNeeded

# Build once
Write-Host "Building llm-cli..." -ForegroundColor Yellow
& $Dotnet build $llmCli --no-restore -q
Write-Host "Build complete." -ForegroundColor Yellow

$useStream = -not $NoStream
$pass = 0
$fail = 0

$prompt      = "Reply with exactly: hello"
$toolPrompt  = "Use the fetch_url tool to fetch https://raw.githubusercontent.com/ianphil/copilot-llm-svc/refs/heads/main/README.md and summarize it in one sentence"

# ══════════════════════════════════════════════════════════════════════════════
# /responses surface (llm ask)
# ══════════════════════════════════════════════════════════════════════════════

# 1. /responses → /responses only model, plain text
if (Invoke-Llm "1. ask → responses-only model, plain" ask $prompt gpt-5.4-mini -Stream:$false) { $pass++ } else { $fail++ }

# 2. /responses → /responses only model, streaming
if (Invoke-Llm "2. ask → responses-only model, streaming" ask $prompt gpt-5.4-mini -Stream:$true) { $pass++ } else { $fail++ }

# 3. /responses → chat-only model, plain text translation
if (Invoke-Llm "3. ask → chat-only model, plain translation" ask $prompt claude-haiku-4.5 -Stream:$false) { $pass++ } else { $fail++ }

# 4. /responses → chat-only model, streaming translation
if (Invoke-Llm "4. ask → chat-only model, streaming translation" ask $prompt claude-haiku-4.5 -Stream:$true) { $pass++ } else { $fail++ }

# 5. /responses → chat-only model, tool definition forwarding
if (Invoke-Llm "5. ask → chat-only model, tools" ask $toolPrompt claude-haiku-4.5 -Stream:$false -Tools) { $pass++ } else { $fail++ }

# 6. /responses → chat-only model, streaming tool round-trip
if (Invoke-Llm "6. ask → chat-only model, streaming + tools" ask $toolPrompt claude-haiku-4.5 -Stream:$true -Tools) { $pass++ } else { $fail++ }

# 7. /responses → dual-endpoint model, should prefer native /responses
if (Invoke-Llm "7. ask → dual-endpoint model, prefers responses" ask $prompt gpt-5-mini -Stream:$false) { $pass++ } else { $fail++ }

# ══════════════════════════════════════════════════════════════════════════════
# /chat/completions surface (llm chat)
# ══════════════════════════════════════════════════════════════════════════════

# 8. /chat/completions → chat-capable model, plain text
if (Invoke-Llm "8. chat → chat-capable model, plain" chat $prompt claude-haiku-4.5 -Stream:$false) { $pass++ } else { $fail++ }

# 9. /chat/completions → chat-capable model, SSE streaming
if (Invoke-Llm "9. chat → chat-capable model, streaming" chat $prompt claude-haiku-4.5 -Stream:$true) { $pass++ } else { $fail++ }

# 10. /chat/completions → chat-capable model, SSE streaming + tools
if (Invoke-Llm "10. chat → chat-capable model, streaming + tools" chat $toolPrompt claude-haiku-4.5 -Stream:$true -Tools) { $pass++ } else { $fail++ }

# 11. /chat/completions → responses-only model, plain text translation
if (Invoke-Llm "11. chat → responses-only model, plain translation" chat $prompt gpt-5.4-mini -Stream:$false) { $pass++ } else { $fail++ }

# 12. /chat/completions → responses-only model, SSE streaming translation
if (Invoke-Llm "12. chat → responses-only model, streaming translation" chat $prompt gpt-5.4-mini -Stream:$true) { $pass++ } else { $fail++ }

# 13. /chat/completions → responses-only model, SSE streaming tool round-trip
if (Invoke-Llm "13. chat → responses-only model, streaming + tools" chat $toolPrompt gpt-5.4-mini -Stream:$true -Tools) { $pass++ } else { $fail++ }

# 14. /chat/completions → dual-endpoint model, should prefer native /chat
if (Invoke-Llm "14. chat → dual-endpoint model, prefers chat" chat $prompt gpt-5-mini -Stream:$false) { $pass++ } else { $fail++ }

# 15. /chat/completions → dual-endpoint model, SSE streaming prefers native
if (Invoke-Llm "15. chat → dual-endpoint model, streaming prefers chat" chat $prompt gpt-5-mini -Stream:$true) { $pass++ } else { $fail++ }

# ══════════════════════════════════════════════════════════════════════════════
# SDK surface (llm sdk-ask / llm sdk-chat)
# ══════════════════════════════════════════════════════════════════════════════

# 16. sdk-ask → direct sdk path, plain text
if (Invoke-Llm "16. sdk-ask → direct sdk path, plain" sdk-ask $prompt gpt-5.4-mini -Stream:$false -UseEndpoint:$false) { $pass++ } else { $fail++ }

# 17. sdk-ask → direct sdk path, streaming
if (Invoke-Llm "17. sdk-ask → direct sdk path, streaming" sdk-ask $prompt gpt-5.4-mini -Stream:$true -UseEndpoint:$false) { $pass++ } else { $fail++ }

# 18. sdk-ask → direct sdk path, tools
if (Invoke-Llm "18. sdk-ask → direct sdk path, tools" sdk-ask $toolPrompt gpt-5.4-mini -Stream:$false -Tools -UseEndpoint:$false) { $pass++ } else { $fail++ }

# 19. sdk-ask → direct sdk path, streaming + tools
if (Invoke-Llm "19. sdk-ask → direct sdk path, streaming + tools" sdk-ask $toolPrompt gpt-5.4-mini -Stream:$true -Tools -UseEndpoint:$false) { $pass++ } else { $fail++ }

# 20. sdk-context-ask → direct SDK Context path, Responses API, plain text
if (Invoke-Llm "20. sdk-context-ask → context API, responses plain" sdk-context-ask $prompt gpt-5.4-mini -Stream:$false -UseEndpoint:$false -ExtraArgs @("--api", "responses")) { $pass++ } else { $fail++ }

# 21. sdk-context-ask → direct SDK Context path, Responses API, streaming
if (Invoke-Llm "21. sdk-context-ask → context API, responses streaming" sdk-context-ask $prompt gpt-5.4-mini -Stream:$true -UseEndpoint:$false -ExtraArgs @("--api", "responses")) { $pass++ } else { $fail++ }

# 22. sdk-context-ask → direct SDK Context path, Chat Completions API, plain text
if (Invoke-Llm "22. sdk-context-ask → context API, chat plain" sdk-context-ask $prompt claude-haiku-4.5 -Stream:$false -UseEndpoint:$false -ExtraArgs @("--api", "chat")) { $pass++ } else { $fail++ }

# 23. sdk-context-ask → direct SDK Context path, Chat Completions API, streaming
if (Invoke-Llm "23. sdk-context-ask → context API, chat streaming" sdk-context-ask $prompt claude-haiku-4.5 -Stream:$true -UseEndpoint:$false -ExtraArgs @("--api", "chat")) { $pass++ } else { $fail++ }

# 24. sdk-chat → direct sdk path, plain text
if (Invoke-Llm "24. sdk-chat → direct sdk path, plain" sdk-chat $prompt gpt-5-mini -Stream:$false -UseEndpoint:$false) { $pass++ } else { $fail++ }

# 25. sdk-chat → direct sdk path, streaming
if (Invoke-Llm "25. sdk-chat → direct sdk path, streaming" sdk-chat $prompt gpt-5-mini -Stream:$true -UseEndpoint:$false) { $pass++ } else { $fail++ }

# ══════════════════════════════════════════════════════════════════════════════
# Summary
# ══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor White
Write-Host "  Results: $pass passed, $fail failed  (of $($pass + $fail))" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
Write-Host "═══════════════════════════════════════" -ForegroundColor White

Stop-StartedProxy

exit $fail
