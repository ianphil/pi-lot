# CLI `chat` Command

## Goal

Add `llm chat` command that talks to the proxy via `/chat/completions`. This gives a manual smoke-testing surface for the chat completions side of the test matrix, symmetric with the existing `llm ask` (which uses `/responses`).

## Usage

```
llm chat "Hello"                          # streaming, default model
llm chat "Hello" -m claude-haiku-4.5      # specific model
llm chat "Hello" --no-stream              # non-streaming
llm chat "Hello" --tools                  # with tool calling
llm chat "Hello" -s "Be concise"          # system prompt
```

## Test Matrix Coverage

With `llm ask` + `llm chat`, a user can manually exercise every cell:

| Matrix scenario | Manual command |
| --- | --- |
| `/responses` native plain | `llm ask "Hi" -m gpt-5.4 --no-stream` |
| `/responses` native streaming | `llm ask "Hi" -m gpt-5.4` |
| `/responses` translated plain | `llm ask "Hi" -m claude-haiku-4.5 --no-stream` |
| `/responses` translated streaming | `llm ask "Hi" -m claude-haiku-4.5` |
| `/responses` tools | `llm ask "Fetch example.com" -m claude-haiku-4.5 --tools` |
| `/chat/completions` native plain | `llm chat "Hi" -m gpt-5-mini --no-stream` |
| `/chat/completions` native streaming | `llm chat "Hi" -m gpt-5-mini` |
| `/chat/completions` translated plain | `llm chat "Hi" -m codex-mini --no-stream` |
| `/chat/completions` translated streaming | `llm chat "Hi" -m codex-mini` |
| `/chat/completions` tools | `llm chat "Fetch example.com" -m gpt-5-mini --tools` |

## Approach

### 1. Create `ChatAgent`

Mirror of `AskAgent` but using `ChatClient` from the OpenAI .NET SDK.

- Constructor takes delegate functions for `completeChatAsync` and `completeChatStreamingAsync` (same testability pattern as `AskAgent`)
- Takes `IToolRegistry` for tool dispatch — reuse existing `FetchUrlTool` + `LocalToolRegistry`
- Tool loop: send messages → if response has `tool_calls` → execute tools → append tool results → send again (up to `MaxToolIterations`)
- The tool definitions need mapping from `ResponseTool` to `ChatTool` (or define a shared tool format)

### 2. Create `ChatRequest`

Record type mirroring `AskRequest`: `ChatRequest(Prompt, Model, SystemInstructions, ToolsEnabled)`. Or just reuse `AskRequest` since the fields are identical.

### 3. Wire `llm chat` in `Program.cs`

- Same arguments as `ask`: `prompt`, `--model`, `--system`, `--no-stream`, `--tools`, `--endpoint`
- Default model: `gpt-5-mini` (a chat-capable model, vs `gpt-5.4-mini` for ask)
- Create `ChatClient` via OpenAI SDK pointing at the proxy endpoint
- Build `ChatAgent`, call `RunStreamingAsync` or `RunNonStreamingAsync`

### 4. Tool registry adaptation

`IToolRegistry.Definitions` returns `ResponseTool[]` (Responses API types). `ChatAgent` needs `ChatTool[]`. Options:
- Add a `ChatDefinitions` property to `IToolRegistry` that returns `ChatTool[]`
- Map `ResponseTool` → `ChatTool` in `ChatAgent` at call time
- Second option is simpler and keeps the tool registry Responses-native

### 5. Tests

- Unit tests for `ChatAgent` following the same delegate-based pattern as existing `AskAgent` tests
- Use queued responses (like `AskAgent` tests use queued `ResponseResult`) but with `ChatCompletion` objects
- Test: non-streaming, streaming, tool round-trip

## Design Notes

- `ChatAgent` is intentionally separate from `AskAgent` — different SDK types, different protocol, clean separation
- Both agents share `IToolRegistry` and `ILocalTool` — tools are registered once, used by either surface
- `llm chat` defaults to a chat-native model so the happy path doesn't require translation
