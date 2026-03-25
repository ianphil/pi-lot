# llm-svc

A local OpenAI-compatible proxy that routes to GitHub Copilot's LLM API using your Copilot CLI credentials. Runs as a console app or Windows service.

## What it does

Exposes a local HTTP API on `http://localhost:5100` that any OpenAI-compatible tool can use:

```
GET  /v1/models              → list available Copilot models
POST /v1/chat/completions    → send chat completion requests
GET  /health                 → service health check
```

**Auto-routing**: Models that only support OpenAI's `/responses` API (GPT-5.4, Codex models) are automatically handled — callers always use the standard `/chat/completions` format, and the service translates internally.

## Prerequisites

- [GitHub Copilot CLI](https://docs.github.com/en/copilot/concepts/agents/about-copilot-cli) installed and logged in (`copilot` → `/login`)
- .NET 10 SDK
- Windows (uses Windows Credential Manager for token retrieval)

## Quick start

```bash
cd llm-svc
dotnet run
```

Then from any tool or script:

```bash
# List models
curl http://localhost:5100/v1/models

# Chat completion
curl http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "claude-haiku-4.5", "messages": [{"role": "user", "content": "Hello!"}]}'
```

## Using with OpenAI SDKs

Point any OpenAI SDK at the local proxy:

```python
from openai import OpenAI

client = OpenAI(base_url="http://localhost:5100/v1", api_key="unused")
response = client.chat.completions.create(
    model="gpt-5.4-mini",
    messages=[{"role": "user", "content": "Hello!"}]
)
print(response.choices[0].message.content)
```

## Installing as a background service

### Option A: Scheduled Task (recommended for domain-joined machines)

On corporate machines, Group Policy often blocks "Log on as a service". Use a scheduled task instead:

```powershell
# Run as Administrator
.\scripts\install-task.ps1
```

This publishes the app, creates a scheduled task that starts at logon, and starts it immediately. Manage with:

```powershell
Stop-ScheduledTask -TaskName CopilotLlmProxy    # stop
Start-ScheduledTask -TaskName CopilotLlmProxy   # start
.\scripts\uninstall-task.ps1                     # remove
```

### Option B: Windows Service

```powershell
# Run as Administrator
.\scripts\install.ps1
```

Then open **services.msc** → find **Copilot LLM Proxy** → **Log On** tab → set to your Windows account → **Start**.

> **Note**: Requires "Log on as a service" right, which may be blocked by Group Policy on domain machines.

## Configuration

Edit `appsettings.json` to change the port:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5100"
      }
    }
  }
}
```

## How it works

1. Reads your Copilot CLI OAuth token from Windows Credential Manager (`copilot-cli/https://github.com:*`)
2. Sends requests directly to `https://api.enterprise.githubcopilot.com`
3. Routes to `/chat/completions` or `/responses` based on model capabilities
4. Translates `/responses` format back to `/chat/completions` format for callers
5. Background worker validates the token every 5 minutes, reloads on 401

## Architecture

```
Caller (any OpenAI SDK)
  │
  ▼
localhost:5100/v1/chat/completions
  │
  ├─ /chat/completions models ──→ Copilot /chat/completions
  │                                (Claude, GPT-5 mini, MiniMax)
  │
  └─ /responses-only models ────→ Copilot /responses
                                   (GPT-5.4, Codex)
                                   └─ translated back to chat.completion format
```
