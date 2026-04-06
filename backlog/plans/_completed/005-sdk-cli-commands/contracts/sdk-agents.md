# SDK Agent Contracts

## SdkAskAgent

Sends a prompt via the Responses API using ILlmSdkClient.

### Methods

```csharp
public static class SdkAskAgent
{
    public static async Task RunNonStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default);

    public static async Task RunStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default);
}
```

### Behavior

**RunNonStreamingAsync:**
1. Build `CreateResponseRequest` from `AskRequest` (model, input as JsonElement, instructions)
2. Call `client.CreateResponseAsync(request)`
3. Extract text via `response.GetOutputText()`
4. If null, write error to stderr; otherwise write text + newline to `writer`

**RunStreamingAsync:**
1. Build `CreateResponseRequest` from `AskRequest` with `Stream = true`
2. Call `client.CreateResponseStreamAsync(request)`
3. For each `ResponseStreamEvent`:
   - `OutputTextDeltaEvent` → write `Delta` to writer
   - `ResponseFailedEvent` → write error to stderr, return
   - `ResponseIncompleteEvent` → write warning to stderr
4. Write newline to writer at end

---

## SdkChatAgent

Sends a prompt via the Chat Completions API using ILlmSdkClient.

### Methods

```csharp
public static class SdkChatAgent
{
    public static async Task RunNonStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default);

    public static async Task RunStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default);
}
```

### Behavior

**RunNonStreamingAsync:**
1. Build `ChatCompletionRequest` from `AskRequest` (model, messages, system)
2. Call `client.CreateChatCompletionAsync(request)`
3. Extract text via `response.GetMessageText()`
4. If null, write error to stderr; otherwise write text + newline to `writer`

**RunStreamingAsync:**
1. Build `ChatCompletionRequest` from `AskRequest` with `Stream = true`
2. Call `client.CreateChatCompletionStreamAsync(request)`
3. For each `ChatCompletionChunk`:
   - Skip if `Choices` is null or empty
   - Skip if `Delta` or `Delta.Content` is null
   - Write non-null content to writer
4. Write newline to writer at end
