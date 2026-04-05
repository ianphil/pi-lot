<#
.SYNOPSIS
    Runs llm CLI commands matching every scenario in the test matrix.
.DESCRIPTION
    Each test-matrix row maps to an llm ask or llm chat invocation that
    exercises the same surface + upstream routing path. Requires the proxy
    to be running at localhost:5100 with valid credentials.
.NOTES
    Models used:
      gpt-5.4-mini     -> /responses only
      gpt-5.4          -> /responses only
      claude-haiku-4.5 -> /chat/completions only
      gpt-5-mini       -> both /chat/completions and /responses (dual)
    Run:  pwsh scripts\test-matrix.ps1
#>

param(
    [string]$Endpoint = "http://localhost:5100",
    [switch]$NoStream
)

$ErrorActionPreference = "Stop"

# Resolve the llm-cli project relative to this script
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$llmCli   = Join-Path $repoRoot "src\llm-cli"

function Invoke-Llm {
    param(
        [string]$Label,
        [string]$Verb,
        [string]$Prompt,
        [string]$Model,
        [switch]$Stream,
        [switch]$Tools
    )

    $cliArgs = @($Verb, $Prompt, "-m", $Model, "-e", $Endpoint)
    if (-not $Stream) { $cliArgs += "--no-stream" }
    if ($Tools)       { $cliArgs += "--tools" }

    Write-Host ""
    Write-Host "─── $Label ───" -ForegroundColor Cyan
    Write-Host "  llm $($cliArgs -join ' ')" -ForegroundColor DarkGray

    try {
        $output = & dotnet run --project $llmCli --no-build -- @cliArgs 2>&1
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

# Build once
Write-Host "Building llm-cli..." -ForegroundColor Yellow
dotnet build $llmCli --no-restore -q
Write-Host "Build complete." -ForegroundColor Yellow

$useStream = -not $NoStream
$pass = 0
$fail = 0

$prompt      = "Reply with exactly: hello"
$toolPrompt  = "Use the fetch_url tool to fetch https://raw.githubusercontent.com/ianphil/faux-foundation/refs/heads/master/README.md and summarize it in one sentence"

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
# Summary
# ══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor White
Write-Host "  Results: $pass passed, $fail failed  (of $($pass + $fail))" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
Write-Host "═══════════════════════════════════════" -ForegroundColor White

exit $fail
