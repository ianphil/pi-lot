# Tools

Tools are declared with `ToolDefinition` and returned as `ToolCallContent`.
After local execution, append a `ToolMessage` with the tool output and call the
model again.

```csharp
using System.Text.Json;
using LlmSdk.Core.Models;

var weatherTool = new ToolDefinition(
    "get_weather",
    "Gets the current weather.",
    JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "city" },
        properties = new
        {
            city = new { type = "string" },
        },
        additionalProperties = false,
    }, JsonDefaults.Web),
    Strict: true);
```

The portable context API validates completed tool-call arguments against the
matching tool schema before returning tool calls. Invalid arguments become
`ToolResultContent` errors so local tool code does not execute malformed input.

For manual validation, use `ToolValidator.Validate(tool, argumentsJson)`. The
validator supports the JSON Schema subset used by SDK tools: `type`, `required`,
`properties`, `additionalProperties`, and `enum`.

## Tool loop shape

1. Send a `Context` with `Tools`.
2. Inspect returned `AssistantMessage.Content` for `ToolCallContent`.
3. Execute local tools only after validation.
4. Append `ToolMessage` values with `ToolCallId` and text output.
5. Call `CompleteAsync` or `StreamAsync` again.
