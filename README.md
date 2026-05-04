# LlmSdk

A .NET library for accessing GitHub Copilot's LLM API with OpenAI-compatible Responses and Chat Completions services. This repository also ships `llm-svc`, a local OpenAI-compatible proxy, `llm`, a CLI reference client for the proxy, and `llm-ui`, an experimental editable-context chat UI.

## What this repo ships

| Component | What it is | Use it when |
|---|---|---|
| **LlmSdk** | Reusable .NET library with auth, model discovery, routing, and translation | You want to embed Copilot-backed LLM access directly in your own .NET app or host |
| **llm-svc** | Local HTTP proxy exposing OpenAI-compatible endpoints on `localhost` | You want existing SDKs, tools, or agents to talk to Copilot through an OpenAI-shaped API |
| **llm** | Terminal client for the proxy | You want a quick terminal workflow or a reference client implementation |
| **llm-ui** | Experimental SPA for editing a Markdown chat transcript/context | You want to edit the conversation context directly and send it through the SDK-backed local API |

## Guides

- `docs/sdk-guide.md` - using `LlmSdk` directly
- `docs/agent-guide.md` - building tool-calling agents with `LlmAgent`
- `docs/cli-guide.md` - using the `llm` CLI
- `docs/api-reference.md` - proxy endpoint reference

## LlmSdk library

`LlmSdk` is the reusable core product in this repo. It handles Copilot credential resolution, request-level credential refresh and retry, model discovery, native `/responses` routing, `/chat/completions` fallback, and translation between the two API shapes.

The library integrates through a single DI entry point:

- `services.AddLlmSdk()`
- `IResponsesService` for Responses API requests
- `IChatCompletionsService` for Chat Completions requests
- `IModelProvider` for model discovery and direct upstream access

If you are working inside this repo, reference `llm-sdk/llm-sdk.csproj`. If you are consuming published packages, use package ID `LlmSdk`.

For higher-level tool-calling orchestration on top of `LlmSdk`, see
`docs/agent-guide.md`.

```csharp
using System.Text.Json;
using LlmSdk;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLlmSdk();

using var provider = services.BuildServiceProvider();

var responses = provider.GetRequiredService<IResponsesService>();
var result = await responses.CreateAsync(new CreateResponseRequest
{
    Model = "gpt-5.4-mini",
    Input = JsonDocument.Parse("\"Hello!\"").RootElement.Clone(),
});

Console.WriteLine(await result.ReadBodyAsync());
```

Credential resolution for the library follows the same order as the proxy:

1. `COPILOT_TOKEN` on every platform
2. Windows Credential Manager entries created by Copilot CLI
3. Linux Secret Service entries created by Copilot CLI

Library consumers do not need to register a background worker for token refresh. `CopilotClient` reloads credentials before requests when needed and retries once on a 401 with a freshly loaded credential.

## llm-svc proxy

`llm-svc` exposes a local HTTP API on `http://localhost:5100` that any OpenAI-compatible tool can use:

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

## llm-svc prerequisites

- [GitHub Copilot CLI](https://docs.github.com/en/copilot/concepts/agents/about-copilot-cli) installed and logged in (`copilot` → `/login`)
- .NET 10 SDK
- Windows or Linux desktop for automatic Copilot credential reuse, or any platform with `COPILOT_TOKEN`

## llm-svc quick start

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

## llm CLI

The `llm` CLI talks to the proxy from your terminal:

```bash
# Ask a question via Responses API (streams by default)
dotnet run --project llm-cli -- ask "What is the capital of France?"

# Chat via Chat Completions API (streams by default)
dotnet run --project llm-cli -- chat "What is the capital of France?"

# Choose a model
dotnet run --project llm-cli -- ask "Write a haiku" -m claude-haiku-4.5
dotnet run --project llm-cli -- chat "Write a haiku" -m gpt-5-mini

# System prompt
dotnet run --project llm-cli -- ask "Review this code" -s "Be direct and concise"

# Use tools (fetch_url)
dotnet run --project llm-cli -- chat "Summarize https://example.com" --tools

# Use the SDK directly — no proxy required
dotnet run --project llm-cli -- sdk-ask "Summarize this change" -m gpt-5.4-mini
dotnet run --project llm-cli -- sdk-ask "Summarize https://example.com" --tools
dotnet run --project llm-cli -- sdk-chat "What is 2+2?" -m gpt-5-mini

# List models and check health
dotnet run --project llm-cli -- models
dotnet run --project llm-cli -- health
```

`llm ask` uses `/v1/responses`. `llm chat` uses `/v1/chat/completions`. Both stream by default and support `--no-stream`, `--model`, `--system`, and `--tools`.

`llm sdk-ask` and `llm sdk-chat` bypass the proxy entirely, calling `ILlmSdkClient` in-process. `sdk-ask` also supports `--tools` via the `llm-agent` loop. SDK commands do not accept `--endpoint`.

Run `llm --help` for full usage, examples, and model guidance.

## llm-ui experiment

`llm-ui` is a local SPA experiment for editing the conversation context as Markdown. The browser talks only to the local ASP.NET Core host; Copilot auth stays server-side through `LlmSdk`. The UI default model is `gpt-5.4`.

Run the API host:

```powershell
dotnet run --project src\llm-ui\llm-ui.csproj
```

Run the Vite dev server in another terminal:

```powershell
Push-Location src\llm-ui\ClientApp
npm install
npm run dev
Pop-Location
```

For a production-style local build:

```powershell
Push-Location src\llm-ui\ClientApp
npm install
npm run build
Pop-Location
dotnet run --project src\llm-ui\llm-ui.csproj
```

The Markdown transcript format uses `## System`, `## User`, and `## Assistant` sections. Edited assistant sections are sent as assistant context, not as user text. Tool sections are reserved for later.

Run the SPA smoke tests with mocked API responses:

```powershell
Push-Location src\llm-ui\ClientApp
npm run smoke
Pop-Location
```

## Using llm-svc with OpenAI SDKs

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

The install script requires an **elevated (Administrator) PowerShell** session:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\scripts\install.ps1"
```

This publishes the app, creates a scheduled task that starts at logon, and starts it immediately. Manage with:

```powershell
Stop-ScheduledTask -TaskName LlmProxy    # stop
Start-ScheduledTask -TaskName LlmProxy   # start
.\scripts\uninstall.ps1                          # remove
```

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

## Authentication

Credential resolution always follows this order:

1. `COPILOT_TOKEN` on every platform
2. Windows Credential Manager entries created by Copilot CLI
3. Linux Secret Service entries created by Copilot CLI

Linux desktop lookup prefers the account referenced by `~/.copilot/config.json` `last_logged_in_user`, but only as non-secret metadata for account selection. The token itself still comes from Secret Service.

Headless or container Linux should use `COPILOT_TOKEN`. If the session bus or keyring is unavailable, the service stays in the existing degraded unauthenticated mode instead of prompting or crashing.

## How it works

1. Resolves your Copilot CLI OAuth token from `COPILOT_TOKEN`, Windows Credential Manager, or Linux Secret Service
2. Sends requests directly to `https://api.enterprise.githubcopilot.com`
3. Routes requests to `/responses` or `/chat/completions` based on model capabilities
4. Translates between Chat Completions and Responses API formats as needed
5. Background worker validates the token every 5 minutes, reloads on 401

`GET /v1/models` preserves upstream-native `supported_endpoints` and adds `proxy_supported_endpoints` so callers can see what Copilot supports directly and what this proxy accepts via translation.

## Architecture

```
Caller (any OpenAI SDK or llm CLI)
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

## Project structure

```
llm-svc/
├── src/
│   ├── llm-sdk/               Reusable library for translation, auth, and upstream access
│   │   ├── ServiceCollectionExtensions.cs
│   │   ├── Client/               SDK surface (LlmSdkClient, options, exceptions)
│   │   ├── Proxy/                Public port interfaces (IResponsesService, IModelProvider)
│   │   ├── Core/                 Domain logic, models, translators
│   │   └── Infrastructure/       HTTP adapters, credential stores
│   ├── llm-svc/                   Host proxy
│   │   ├── Program.cs             Composition root (calls AddLlmSdk)
│   │   └── Worker.cs              Background auth lifecycle
│   └── llm-cli/                   CLI client (System.CommandLine + OpenAI SDK + LlmSdk)
├── tests/
│   ├── llm-sdk.Tests/         Library unit tests
│   ├── llm-svc.Tests/            Host integration + smoke tests
│   └── llm-cli.Tests/            CLI tests
├── Directory.Build.props          Shared build properties
└── copilot-llm.sln
```

## Testing

```bash
# Run CI-safe library and host tests
dotnet test tests/llm-sdk.Tests/llm-sdk.Tests.csproj
dotnet test tests/llm-svc.Tests/llm-svc.Tests.csproj --filter "Category!=Smoke"

# Run live smoke tests (requires running proxy with credentials)
dotnet test --filter "Category=Smoke"
```
