# Streaming

The SDK exposes two streaming levels:

| Surface | Event type | Use it when |
|---|---|---|
| `StreamAsync(Context, ...)` | `AssistantStreamEvent` | You want portable text, thinking, tool, usage, and terminal events |
| `CreateResponseStreamAsync(...)` | `ResponseStreamEvent` | You need raw Responses SSE event fidelity |
| `CreateChatCompletionStreamAsync(...)` | `ChatCompletionChunk` | You need raw Chat Completions chunks |

## Portable streaming

```csharp
await foreach (var evt in client.StreamAsync(context, new CompletionOptions
{
    Model = "gpt-5.4-mini",
}))
{
    switch (evt)
    {
        case TextDelta text:
            Console.Write(text.Text);
            break;
        case ToolCallDelta tool:
            Console.WriteLine($"Tool: {tool.Name} {tool.ArgumentsJsonChunk}");
            break;
        case StreamDone done:
            Console.WriteLine($"\nStopped: {done.FinalMessage.StopReason}");
            break;
        case StreamError error:
            Console.Error.WriteLine(error.Message);
            break;
    }
}
```

`ToolCallDelta.ParsedSoFar` is populated when the accumulated argument chunks can
be repaired into valid JSON. Use the final `StreamDone.FinalMessage` as the
source of truth before executing tools.

## Abort behavior

`AbortMode.ReturnPartial` returns terminal `StreamError` or `StreamDone` events
with the partial assistant message after recoverable interruptions. Set
`AbortMode.Throw` when callers need exceptions instead of partials.
