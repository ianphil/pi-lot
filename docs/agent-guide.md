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
        case MessageDelta { StreamEvent: OutputTextDeltaEvent delta }:
            Console.Write(delta.Delta);
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
- `MessageStarted` / `MessageDelta` / `MessageEnded`
- `ToolExecutionStarted` / `ToolExecutionEnded`

For text streaming, handle `MessageDelta` and look for `OutputTextDeltaEvent`.
For tool progress, handle the `ToolExecution*` events.

## Tool authoring

Each tool supplies:

- `Name` — must match what the model calls
- `Description` — shown to the model
- `Parameters` — JSON Schema as `JsonElement`
- `Strict` — whether schema validation should be strict
- `ExecuteAsync(...)` — receives parsed `JsonElement` arguments

If a tool throws, the loop catches the exception and sends an error result back
to the model as tool output. If the model calls an unknown tool, the loop does
the same with a "tool not found" result.

## Request behavior

`LlmAgent` uses the Responses streaming API underneath:

- the user prompt becomes the first context item
- `AgentLoopOptions.Instructions` maps to `CreateResponseRequest.Instructions`
- tool definitions map to `CreateResponseRequest.Tools`
- completed response output items are appended to context
- tool results are appended as `function_call_output` items

The loop is client-side and stateless. It does not use `previous_response_id`.

## Reference implementation in this repo

`llm sdk-ask --tools` is the reference CLI consumer of `LlmAgent`. It layers
the agent loop on top of `ILlmSdkClient` and adapts CLI local tools into
`IAgentTool` instances.

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
