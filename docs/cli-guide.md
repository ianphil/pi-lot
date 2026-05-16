# llm CLI Guide

`llm` is a small terminal smoke/reference client for querying language models
through the local Copilot LLM proxy. It supports the command surface exercised by
the live CLI matrix: `ask`, `chat`, `health`, proxy endpoint selection, per-call
proxy knobs, streaming, and the built-in `fetch_url` tool.

SDK correctness belongs in `tests/llm-sdk.Int`; service/proxy correctness
belongs in `tests/llm-svc.Int`. The CLI intentionally does not expose direct SDK
commands or model-discovery helpers.

## Prerequisites

- .NET 10 SDK
- The proxy (`llm-svc`) running and authenticated

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

### health

Check if the proxy is running and authenticated.

```bash
llm health
```

Returns status (`healthy` / `degraded`), authentication state, and endpoint URL.
A `degraded` status means the proxy is running but credentials are missing.

## Flags reference

| Flag | Short | Commands | Default | Description |
|---|---|---|---|---|
| `<prompt>` | | `ask`, `chat` | *(required)* | The prompt to send |
| `--model` | `-m` | `ask`, `chat` | varies | Model ID |
| `--system` | `-s` | `ask`, `chat` | none | System instructions |
| `--no-stream` | | `ask`, `chat` | `false` | Disable streaming |
| `--tools` | | `ask`, `chat` | `false` | Enable local tools |
| `--request-id` | | `ask`, `chat` | generated | Request ID sent upstream as `X-Request-Id` |
| `--correlation-id` | | `ask`, `chat` | none | Local proxy correlation ID |
| `--metadata` | | `ask`, `chat` | none | Local metadata as repeatable `key=value` |
| `--timeout-ms` | | `ask`, `chat` | SDK default | Per-call upstream timeout |
| `--max-retries` | | `ask`, `chat` | SDK default | Per-call retry count |
| `--max-retry-delay-ms` | | `ask`, `chat` | SDK default | Per-call retry delay cap |
| `--endpoint` | `-e` | `ask`, `chat`, `health` | `http://localhost:5100` | Base URL of the proxy |

The `--endpoint` flag is a per-command flag, not a root flag:

```bash
llm health -e http://localhost:5200
llm ask "Hello" -e http://localhost:5200
```

## Tool calling

The `--tools` flag enables local tool execution during agent loops. The model can
request tool calls, and the CLI executes them locally before sending results
back. Available on `ask` and `chat`.

### fetch_url

Fetches the contents of an HTTP or HTTPS URL and returns readable text.

- Strips HTML tags, scripts, and styles from web pages
- Truncates content to 20,000 characters
- 20-second timeout per request
- Supports text, HTML, JSON, and XML content types

```bash
llm ask "Summarize this page: https://openresponses.org/specification" --tools
llm chat "What does this API do? https://api.example.com/docs" --tools
```

## Examples

```bash
# Quick question
llm ask "What is the capital of France?"

# Use a specific model
llm ask "Explain monads in simple terms" -m gpt-5.4

# System prompt for persona
llm ask "Review this code" -s "You are a senior .NET engineer. Be direct."

# Pipe-friendly output
llm ask "Generate a UUID" --no-stream | clip

# Fetch and summarize a web page
llm ask "Summarize https://openresponses.org/specification" --tools

# Chat Completions API
llm chat "What is 2+2?" -m gpt-5-mini

# Verify proxy is up, then ask
llm health && llm ask "Ready to go"
```

## How it works

`ask` and `chat` use the [OpenAI .NET SDK](https://github.com/openai/openai-dotnet)
to talk to the proxy over HTTP:

- `ask` creates a `ResponsesClient` pointing at the proxy endpoint
- `chat` creates a `ChatClient` pointing at the proxy endpoint
- Both pass `"unused"` as the API key because the proxy handles auth

The agent loop supports multi-turn tool calling: the model requests a tool call,
the CLI executes it locally, sends the result back, and the model continues.

## Routing

```text
llm ask  -> proxy /v1/responses        -> upstream (native or translated)
llm chat -> proxy /v1/chat/completions -> upstream (native or translated)
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Error (invalid arguments, API error) |

> **Note:** `health` always exits `0` — it prints status information
> (including `unreachable`) but does not fail. Use the printed status to
> determine proxy availability.
