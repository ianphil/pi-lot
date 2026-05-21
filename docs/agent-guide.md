# LlmAgent Guide

`LlmAgent` is a .NET library for building tool-calling agents on top of
`LlmSdk`. It owns the agent loop: stream a response, detect tool calls, execute
local tools, feed results back into the next turn, and keep going until the
model stops asking for tools.

## Installation

As a project reference within this repo:

```xml
<!-- from src/llm-cli/ -->
<ProjectReference Include="..\llm-agent\llm-agent.csproj" />
<ProjectReference Include="..\llm-sdk\llm-sdk.csproj" />
```

`LlmAgent` depends on `LlmSdk`; you need both.

## Quick start

```csharp
using System.Text.Json;
using LlmAgent;
using LlmSdk;
using LlmSdk.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddLlmSdk(options => options.DefaultModel = "gpt-5.4-mini");

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ILlmSdkClient>();

var options = new AgentLoopOptions
{
    Model = "gpt-5.4-mini",
    Instructions = "You are a helpful coding assistant.",
    Tools = [new ReadFileTool()],
    MaxTurns = 8,
};

await foreach (var evt in AgentLoop.RunAsync(client, "Read README.md and summarize it.", options))
{
    switch (evt)
    {
        case MessageDelta { StreamEvent: TextDelta delta }:
            Console.Write(delta.Text);
            break;

        case ToolExecutionStarted(var callId, var toolName, _):
            Console.WriteLine($"\n[tool:start] {toolName} ({callId})");
            break;

        case ToolExecutionEnded(_, var toolName, var result):
            Console.WriteLine($"\n[tool:end] {toolName}: {result.Content}");
            break;

        case AgentEnded(var context):
            Console.WriteLine($"\nDone. Context items: {context.Items.Count}");
            break;
    }
}

sealed class ReadFileTool : IAgentTool
{
    public string Name => "read_file";

    public string Description => "Read a UTF-8 text file from disk.";

    public JsonElement? Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string" },
        },
        required = new[] { "path" },
        additionalProperties = false,
    });

    public bool? Strict => true;

    public Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()
            ?? throw new InvalidOperationException("Missing path.");

        var content = File.ReadAllText(path);
        return Task.FromResult(new AgentToolResult(content));
    }
}
```

> **Important:** Call `services.AddLogging()` before `services.AddLlmSdk()`.

---

## Core types

| Type | Purpose |
|---|---|
| `AgentLoop` | Static entry point. Runs the agent loop and returns `IAsyncEnumerable<AgentEvent>`. |
| `AgentLoopOptions` | Model, instructions, tools, turn limit, and request options. |
| `IAgentTool` | Tool definition plus executable `ExecuteAsync` method. |
| `AgentEvent` | Stream of agent lifecycle, message, and tool execution events. |
| `AgentContext` | Final typed context accumulated across turns. |

## Event stream

`AgentLoop.RunAsync(...)` emits a structured event stream so UIs and CLIs can
stay responsive while the model runs.

The main events are:

- `AgentStarted` / `AgentEnded`
- `TurnStarted` / `TurnEnded`
- `MessageStarted` / `MessageDelta` / `MessageUsage` / `MessageDiagnostics` / `MessageEnded`
- `ToolExecutionStarted` / `ToolExecutionEnded`

For text streaming, handle `MessageDelta` and look for the portable SDK
`TextDelta` event. Thinking/reasoning deltas are also surfaced through
`MessageDelta` as portable SDK `ThinkingDelta` events, and final reasoning
content is preserved in the final `AssistantMessage` as `ThinkingContent` when
the selected SDK path provides it.

`MessageUsage` reports normalized SDK token usage observed during streaming.
`MessageDiagnostics` reports structured SDK diagnostics attached to the terminal
assistant message, such as partial-response or request-adaptation warnings.
Callers do not need to parse raw SDK stream internals to observe those values.

`MessageEnded.Status` and `AgentEnded.Status` describe terminal semantics using
the single `AgentStatus` enum:

| Condition | Status | Notes |
|---|---|---|
| Normal model stop or tool use | `Completed` | The message is complete for this turn. |
| SDK `StopReason.Length` | `Incomplete` | The assistant message is partial because output stopped due to length. |
| `MaxTurns` reached before a terminal turn | `Incomplete` (run only) | Loop budget exhausted; `AgentEnded.ErrorMessage` describes it. |
| SDK-produced aborted result (e.g. external `CancellationToken` cancel mid-stream under default `AbortMode.ReturnPartial`) | `Cancelled` | The SDK adapter converts a mid-stream `OperationCanceledException` into `StreamDone(StopReason.Aborted)` and the agent surfaces it as `Cancelled`. |
| SDK `StreamError` or `StopReason.Error` | `Failed` | The terminal message may contain partial assistant content and diagnostics. |

`MessageEnded.IsPartial` is `true` whenever `Status` is not `Completed`; it is
derived from `Status` so the two cannot disagree. `MessageEnded.ErrorMessage`
and `AgentEnded.ErrorMessage` carry the recoverable stream error message (or
`MaxTurns` reason) when one is available.

### Cancellation semantics

The agent does **not** introduce its own `AbortMode`. It inherits the SDK
default (`AbortMode.ReturnPartial`):

- A `CancellationToken` cancelled **mid-stream** is caught inside the SDK
  adapter and converted into a terminal `StreamDone(StopReason.Aborted)`. The
  agent surfaces this as `MessageEnded { Status = Cancelled }` and
  `AgentEnded { Status = Cancelled }` — it does **not** throw
  `OperationCanceledException`.
- A `CancellationToken` already cancelled **between turns** (checked by the
  loop itself before the next request) throws `OperationCanceledException` and
  does not guarantee `MessageEnded`, `TurnEnded`, or `AgentEnded` events.

`AbortMode` is intentionally not exposed on `AgentLoopOptions` in this
release. Callers who need throw-on-cancel semantics should treat that as a
future agent option request, not as a per-call override.

For tool progress, handle the `ToolExecution*` events.

## Tool authoring

Each tool supplies:

- `Name` — must match what the model calls
- `Description` — shown to the model
- `Parameters` — JSON Schema as `JsonElement`; enforced before local execution
- `Strict` — whether schema validation should be strict
- `ExecuteAsync(...)` — receives parsed arguments only after JSON and schema
  validation pass

Before local tool code runs, the agent parses the final model-produced arguments
and validates them against the matching tool's `Parameters` schema using the SDK
validator. Invalid JSON or schema-invalid arguments become agent-owned tool error
results; the local `ExecuteAsync(...)` method is not called. If a tool throws,
the loop catches the exception and sends an error result back to the model as
tool output. If the model calls an unknown tool, the loop does the same with a
"tool not found" result.

Streamed tool-call argument chunks are observable through `MessageDelta` as
portable SDK `ToolCallDelta` events. Local tool progress callbacks and pre/post
tool policy hooks are separate future agent API stories.

## Request behavior

`LlmAgent` uses the SDK portable context streaming API underneath:

- the user prompt becomes the first context item
- `AgentLoopOptions.Instructions` maps to `Context.System`
- tool definitions map to portable `Context.Tools`
- `Headers`, `PromptCacheKey`, `SessionId`, `CacheRetention`, `OnPayload`, and
  `OnResponse` forward through `CompletionOptions` for every agent turn
- request IDs, correlation IDs, metadata, timeout, and retry options also forward
  to every agent turn
- completed, incomplete, cancelled, and failed assistant messages are appended to context when the SDK produces a terminal assistant message
- tool results are appended as portable tool messages

The loop is client-side and stateless. It does not use `previous_response_id`.
Raw Responses details are an advanced/debug escape hatch rather than the primary
agent harness surface.

`OnPayload` and `OnResponse` are per-turn hooks. A tool-using run may invoke
them multiple times because each follow-up turn sends another Responses request.
Use `OnPayload` primarily for inspection or controlled rewrites; returning a
replacement payload changes the request sent for that turn.

## Reference consumers in this repo

`tests/llm-agent.Tests` covers pure event-shape and edge-case behavior.
`tests/llm-agent.Int` is the fake/live reference consumer for `LlmAgent`: it
exercises the public agent loop on top of `ILlmSdkClient` with a deterministic
fake SDK client and with the live SDK/upstream path.

See `docs/cli-guide.md` for the CLI surface and `docs/sdk-guide.md` for the
lower-level SDK client used underneath.

## Current limitations

Version `0.1.0` is intentionally small:

- stateless loop only; no stateful `Agent` wrapper
- sequential tool execution only
- no before/after tool hooks
- no context window management
- no custom message types beyond Responses API items

That makes it a good library seam for real consumers while keeping the first
version easy to reason about and test.
