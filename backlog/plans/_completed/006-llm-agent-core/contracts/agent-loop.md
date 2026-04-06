# Agent Loop Contract

## AgentLoop

Static class providing the stateless agent loop.

### Methods

```csharp
namespace LlmAgent;

public static class AgentLoop
{
    public static IAsyncEnumerable<AgentEvent> RunAsync(
        ILlmSdkClient client,
        string prompt,
        AgentLoopOptions options,
        CancellationToken cancellationToken = default);
}
```

### Behavior

**RunAsync:**

1. Emit `AgentStarted`
2. Initialize context with typed `UserMessageItem` from prompt
3. Enter turn loop:
   a. Emit `TurnStarted`
   b. Build `CreateResponseRequest`: serialize context items → `JsonElement` for `Input`
   c. Call `client.CreateResponseStreamAsync(request, cancellationToken)`
   d. Emit `MessageStarted`
   e. For each `ResponseStreamEvent`: emit `MessageDelta`
   f. On `ResponseCompletedEvent`: emit `MessageEnded` with full `Response`
   g. On `ResponseFailedEvent` or `ResponseIncompleteEvent`: emit `MessageEnded`, break loop
   h. Extract `ResponseFunctionCallItem[]` from `Response.Output`
   i. If no tool calls: emit `TurnEnded`, break loop
   j. Append response output items to context (as typed items)
   k. For each tool call:
      - Find matching `IAgentTool` by name
      - If not found: emit `ToolExecutionStarted` + `ToolExecutionEnded` with error result `"Tool '{name}' not found."`
      - If found: emit `ToolExecutionStarted`, parse arguments to `JsonElement`, call `ExecuteAsync`, emit `ToolExecutionEnded`
      - Append `FunctionCallOutputItem` to context
   l. Emit `TurnEnded`
   m. Continue loop
4. Emit `AgentEnded` with final context

### Error Handling

- **Tool throws exception:** Caught, wrapped as `AgentToolResult { Content = exception.Message, IsError = true }`, fed back to model
- **Tool not found:** Error result returned to model via `ToolExecutionEnded` with `IsError = true`
- **Invalid tool arguments (bad JSON):** Error result returned to model: `"Invalid arguments: {message}"`
- **Stream fails (ResponseFailedEvent):** Loop terminates, emits `TurnEnded` + `AgentEnded`
- **Stream incomplete (ResponseIncompleteEvent):** Loop terminates, emits `TurnEnded` + `AgentEnded`
- **Cancellation:** Checked before each turn; throws `OperationCanceledException` which propagates through `IAsyncEnumerable`
- **MaxTurns exceeded:** Loop terminates normally, emits `AgentEnded`

---

## IAgentTool

Interface for executable tools.

```csharp
namespace LlmAgent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement? Parameters { get; }
    bool? Strict { get; }

    Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}
```

### Behavior

- `Name`, `Description`, `Parameters`, `Strict` map to `ResponseFunctionToolDefinition` fields
- `ExecuteAsync` receives parsed `JsonElement` arguments (loop parses once from raw JSON string)
- Implementations throw on failure — the loop catches and reports to model
- `callId` is provided for correlation (logging, progress tracking)

### Helper

```csharp
internal static ResponseFunctionToolDefinition ToToolDefinition(this IAgentTool tool)
{
    return new ResponseFunctionToolDefinition
    {
        Name = tool.Name,
        Description = tool.Description,
        Parameters = tool.Parameters,
        Strict = tool.Strict,
    };
}
```

---

## AgentToolResult

```csharp
namespace LlmAgent;

public sealed record AgentToolResult(string Content, bool IsError = false);
```

---

## AgentLoopOptions

```csharp
namespace LlmAgent;

public sealed record AgentLoopOptions
{
    public required string Model { get; init; }
    public string? Instructions { get; init; }
    public IReadOnlyList<IAgentTool> Tools { get; init; } = [];
    public int? MaxTurns { get; init; }
    public double? Temperature { get; init; }
    public ResponseReasoning? Reasoning { get; init; }
}
```

---

## AgentEvent Hierarchy

9 event types (3 layers):

```csharp
namespace LlmAgent;

public abstract record AgentEvent;

// Agent lifecycle
public sealed record AgentStarted : AgentEvent;
public sealed record AgentEnded(AgentContext Context) : AgentEvent;

// Turn lifecycle
public sealed record TurnStarted : AgentEvent;
public sealed record TurnEnded(Response Response, IReadOnlyList<AgentToolCallResult> ToolResults) : AgentEvent;

// Message lifecycle
public sealed record MessageStarted : AgentEvent;
public sealed record MessageDelta(ResponseStreamEvent StreamEvent) : AgentEvent;
public sealed record MessageEnded(Response Response) : AgentEvent;

// Tool lifecycle
public sealed record ToolExecutionStarted(string CallId, string ToolName, string Arguments) : AgentEvent;
public sealed record ToolExecutionEnded(string CallId, string ToolName, AgentToolResult Result) : AgentEvent;
```

---

## AgentContext

Typed context model. Serialized to `JsonElement` only at the `CreateResponseRequest.Input` boundary.

```csharp
namespace LlmAgent;

public sealed class AgentContext
{
    private readonly List<AgentContextItem> _items = [];

    public IReadOnlyList<AgentContextItem> Items => _items;

    public void AddUserMessage(string text);
    public void AddResponseOutput(ResponseItem[] outputItems);
    public void AddToolResult(string callId, string output);

    internal JsonElement SerializeInput();
}
```

### AgentContextItem

```csharp
namespace LlmAgent;

public abstract record AgentContextItem;
public sealed record UserMessageContextItem(string Text) : AgentContextItem;
public sealed record ResponseOutputContextItem(ResponseItem Item) : AgentContextItem;
public sealed record ToolResultContextItem(string CallId, string Output) : AgentContextItem;
```

### Serialization

`SerializeInput()` builds a `JsonElement` array for `CreateResponseRequest.Input`:
- `UserMessageContextItem` → `{ "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "..." }] }`
- `ResponseOutputContextItem` → serialized `ResponseItem` (already has polymorphic JSON support)
- `ToolResultContextItem` → `{ "type": "function_call_output", "call_id": "...", "output": "..." }` (ID generated with `Guid.NewGuid()`)

---

## AgentToolCallResult

```csharp
namespace LlmAgent;

public sealed record AgentToolCallResult(
    string CallId,
    string ToolName,
    string Output,
    bool IsError);
```
