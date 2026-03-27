# llm-svc

A local OpenAI-compatible proxy that routes to GitHub Copilot's LLM API using your Copilot CLI credentials. Runs as a console app or Windows service.

## What it does

Exposes a local HTTP API on `http://localhost:5100` that any OpenAI-compatible tool can use:

```
GET  /v1/models              → list available models
GET  /models                 → SDK-friendly alias for model listing
POST /v1/responses           → send OpenAI Responses API requests
POST /responses              → SDK-friendly alias for responses
POST /v1/chat/completions    → send chat completion requests
POST /chat/completions       → SDK-friendly alias for chat completions
GET  /health                 → service health check
```

**Auto-routing**:
- `/v1/responses` is the unified surface. Models that only support `/chat/completions` are translated into Responses API output.
- `/v1/chat/completions` remains available for compatibility, and `/responses`-only models are translated back internally when needed.

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

# Responses API
curl http://localhost:5100/v1/responses \
  -H "Content-Type: application/json" \
  -d '{"model": "gpt-5.4", "input": "Hello!"}'

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
response = client.responses.create(
    model="gpt-5.4-mini",
    input="Hello!"
)
print(response.output[0].content[0].text)
```

For the OpenAI .NET SDK, use the service root as the custom endpoint:

```csharp
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;

var client = new ResponsesClient(
    new ApiKeyCredential("unused"),
    new OpenAIClientOptions
    {
        Endpoint = new Uri("http://localhost:5100")
    });

var options = new CreateResponseOptions
{
    Model = "claude-haiku-4.5",
};
options.InputItems.Add(ResponseItem.CreateUserMessageItem("Hello!"));

var response = await client.CreateResponseAsync(options);
Console.WriteLine(((MessageResponseItem)response.OutputItems[0]).Content[0].Text);
```

Chat Completions clients still work too:

```python
from openai import OpenAI

client = OpenAI(base_url="http://localhost:5100/v1", api_key="unused")
response = client.chat.completions.create(
    model="claude-haiku-4.5",
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
3. Routes requests to `/responses` or `/chat/completions` based on model capabilities
4. Translates between Chat Completions and Responses API formats as needed
5. Background worker validates the token every 5 minutes, reloads on 401

`GET /v1/models` preserves upstream-native `supported_endpoints` and adds `proxy_supported_endpoints` so callers can see what Copilot supports directly and what this proxy accepts via translation.

## Architecture

```
Caller (any OpenAI SDK)
  │
  ▼
localhost:5100/v1/responses
  │
  ├─ native /responses models ──→ Copilot /responses
  │                                (GPT-5.4, Codex, GPT-5.1/5.2)
  │
  └─ chat-only models ──────────→ Copilot /chat/completions
                                   (Claude, MiniMax)
                                   └─ translated into Responses API format
```
