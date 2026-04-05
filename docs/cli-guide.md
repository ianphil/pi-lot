# llm CLI Guide

`llm` is a terminal client for querying language models through the local
Copilot LLM proxy or directly through the LlmSdk. It supports streaming,
system prompts, model selection, and local tool calling.

## Prerequisites

- .NET 10 SDK
- For `ask`, `chat`, `models`, `health`: the proxy (`llm-svc`) must be running
- For `sdk-ask`, `sdk-chat`: no proxy required — calls Copilot directly, but
  still requires Copilot credentials (`COPILOT_TOKEN` or a supported credential store)

## Running

From the repo root:

```bash
dotnet run --project src/llm-cli -- <command> [options]
```

Or install the CLI tool:

```powershell
scripts\install-cli.ps1
```

After installation, use `llm` directly:

```bash
llm ask "What is the capital of France?"
```

---

## Commands

### ask

Send a prompt via the Responses API. Streams by default.

```bash
llm ask "your prompt"
llm ask "your prompt" --no-stream
llm ask "your prompt" -m gpt-5.4
llm ask "your prompt" -s "Be concise"
llm ask "summarize https://example.com" --tools
```

Default model: `gpt-5.4-mini`

The proxy routes the request based on model capability. Native `/responses`
models pass through directly; chat-only models (e.g., Claude) are translated
automatically.

### chat

Send a prompt via the Chat Completions API. Streams by default.

```bash
llm chat "your prompt"
llm chat "your prompt" --no-stream
llm chat "your prompt" -m gpt-5-mini
llm chat "your prompt" -s "Be concise"
llm chat "summarize https://example.com" --tools
```

Default model: `gpt-5-mini`

Same behavior as `ask` but uses `/v1/chat/completions`. Models that only
support `/responses` upstream are translated back internally.

### sdk-ask

Send a prompt directly through `ILlmSdkClient.CreateResponseAsync` /
`CreateResponseStreamAsync`. No proxy required — calls the Copilot API
in-process.

```bash
llm sdk-ask "your prompt"
llm sdk-ask "your prompt" --no-stream
llm sdk-ask "your prompt" -m gpt-5.4
llm sdk-ask "your prompt" -s "Be concise"
```

Default model: `gpt-5.4-mini`

### sdk-chat

Send a prompt directly through `ILlmSdkClient.CreateChatCompletionAsync` /
`CreateChatCompletionStreamAsync`. No proxy required.

```bash
llm sdk-chat "your prompt"
llm sdk-chat "your prompt" --no-stream
llm sdk-chat "your prompt" -m gpt-5-mini
llm sdk-chat "your prompt" -s "Be concise"
```

Default model: `gpt-5-mini`

### models

List available models with their supported endpoints.

```bash
llm models
```

Output shows model ID, display name, and supported endpoint list.

### health

Check if the proxy is running and authenticated.

```bash
llm health
```

Returns status (`healthy` / `degraded`), authentication state, and endpoint
URL. A `degraded` status means the proxy is running but credentials are
missing.

---

## Flags reference

### Per-command flags

| Flag | Short | Commands | Default | Description |
|---|---|---|---|---|
| `<prompt>` | | all except `models`, `health` | *(required)* | The prompt to send (positional) |
| `--model` | `-m` | `ask`, `chat`, `sdk-ask`, `sdk-chat` | varies | Model ID |
| `--system` | `-s` | `ask`, `chat`, `sdk-ask`, `sdk-chat` | none | System instructions |
| `--no-stream` | | `ask`, `chat`, `sdk-ask`, `sdk-chat` | `false` | Disable streaming |
| `--tools` | | `ask`, `chat` | `false` | Enable local tools |

### Shared proxy-command flags

| Flag | Short | Default | Description |
|---|---|---|---|
| `--endpoint` | `-e` | `http://localhost:5100` | Base URL of the proxy |

The `--endpoint` flag is available on `ask`, `chat`, `models`, and `health`.
It is a per-command flag, not a root flag — place it after the subcommand:

```bash
llm health -e http://localhost:5200
llm ask "Hello" -e http://localhost:5200
```

SDK commands (`sdk-ask`, `sdk-chat`) bypass the proxy entirely and do not
accept `--endpoint`.

### Default models

| Command | Default model |
|---|---|
| `ask`, `sdk-ask` | `gpt-5.4-mini` |
| `chat`, `sdk-chat` | `gpt-5-mini` |

---

## Tool calling

The `--tools` flag enables local tool execution during agent loops. The model
can request tool calls, and the CLI executes them locally before sending results
back. Available on `ask` and `chat` commands only.

### Built-in tools

#### fetch_url

Fetches the contents of an HTTP or HTTPS URL and returns readable text.

- Strips HTML tags, scripts, and styles from web pages
- Truncates content to 20,000 characters
- 20-second timeout per request
- Supports text, HTML, JSON, and XML content types

```bash
llm ask "Summarize this page: https://openresponses.org/specification" --tools
llm chat "What does this API do? https://api.example.com/docs" --tools
```

The model decides when to call `fetch_url` based on the prompt. You don't
invoke it directly — the `--tools` flag simply makes it available to the model.

---

## Examples

```bash
# Quick question
llm ask "What is the capital of France?"

# Use a powerful model
llm ask "Explain monads in simple terms" -m gpt-5.4

# System prompt for persona
llm ask "Review this code" -s "You are a senior .NET engineer. Be direct."

# Pipe-friendly output (no streaming, clean text)
llm ask "Generate a UUID" --no-stream | clip

# Use Claude via the translation layer
llm ask "Write a haiku about shipping code" -m claude-haiku-4.5

# Fetch and summarize a web page
llm ask "Summarize https://openresponses.org/specification" --tools

# Chat Completions API
llm chat "What is 2+2?" -m gpt-5-mini

# Chat with tools
llm chat "Summarize https://example.com" --tools

# SDK direct — no proxy needed
llm sdk-ask "Summarize this change" -m gpt-5.4-mini
llm sdk-chat "What is 2+2?" -m gpt-5-mini

# Check available models
llm models

# Verify proxy is up, then ask
llm health && llm ask "Ready to go"
```

---

## How it works

### Proxy commands (ask, chat)

`ask` and `chat` use the [OpenAI .NET SDK](https://github.com/openai/openai-dotnet)
to talk to the proxy over HTTP:

- `ask` creates a `ResponsesClient` pointing at the proxy endpoint
- `chat` creates a `ChatClient` pointing at the proxy endpoint
- Both pass `"unused"` as the API key — the proxy handles auth

The agent loop supports multi-turn tool calling: the model requests a tool call,
the CLI executes it locally, sends the result back, and the model continues.

### SDK commands (sdk-ask, sdk-chat)

`sdk-ask` and `sdk-chat` build an in-process `ServiceProvider` with
`AddLogging()` and `AddLlmSdk()`, then resolve `ILlmSdkClient` directly. No
proxy needed — they call the Copilot API through the SDK's HTTP adapter.
Credentials are still required via `COPILOT_TOKEN` or a platform credential
store.

### Routing

```
llm ask  → proxy /v1/responses       → upstream (native or translated)
llm chat → proxy /v1/chat/completions → upstream (native or translated)

llm sdk-ask  → ILlmSdkClient → IResponsesService       → upstream
llm sdk-chat → ILlmSdkClient → IChatCompletionsService  → upstream
```

---

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Error (invalid arguments, API error) |

> **Note:** `health` always exits `0` — it prints status information
> (including `unreachable`) but does not fail. Use the printed status to
> determine proxy availability.
