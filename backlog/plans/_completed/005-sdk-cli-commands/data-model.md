# Data Model: SDK CLI Commands

## Entities

This feature introduces no new data entities. It reuses existing models from
the LlmSdk library and the CLI's AskRequest DTO.

### AskRequest (Existing — Reused)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Prompt | string | Yes | — | User's input prompt |
| Model | string | Yes | gpt-5.4-mini | Model identifier |
| SystemInstructions | string? | No | null | System prompt |
| ToolsEnabled | bool | Yes | false | Always false for SDK commands |

### CreateResponseRequest (LlmSdk — Consumed)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Model | string? | No | — | Model name (resolved by SDK if null) |
| Input | JsonElement | Yes | — | User input content |
| Instructions | string? | No | null | System instructions |
| Stream | bool? | No | null | Streaming mode flag |

### ChatCompletionRequest (LlmSdk — Consumed)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Model | string? | No | — | Model name |
| Messages | List&lt;ChatMessage&gt; | Yes | — | Conversation messages |
| Stream | bool? | No | null | Streaming mode flag |

## Data Flow

### sdk-ask (Non-Streaming)

```
AskRequest
    │
    ▼
CreateResponseRequest { Model, Input = prompt, Instructions = system }
    │
    ▼
ILlmSdkClient.CreateResponseAsync()
    │
    ▼
Response
    │
    ▼
response.GetOutputText() → stdout
```

### sdk-ask (Streaming)

```
AskRequest
    │
    ▼
CreateResponseRequest { Model, Input = prompt, Instructions = system, Stream = true }
    │
    ▼
ILlmSdkClient.CreateResponseStreamAsync()
    │
    ▼
IAsyncEnumerable<ResponseStreamEvent>
    │
    ├── OutputTextDeltaEvent → write delta to stdout
    ├── ResponseCompletedEvent → done
    ├── ResponseFailedEvent → write error to stderr
    └── ResponseIncompleteEvent → write warning to stderr
```

### sdk-chat (Non-Streaming)

```
AskRequest
    │
    ▼
ChatCompletionRequest { Model, Messages = [system?, user] }
    │
    ▼
ILlmSdkClient.CreateChatCompletionAsync()
    │
    ▼
ChatCompletionResponse
    │
    ▼
response.GetMessageText() → stdout
```

### sdk-chat (Streaming)

```
AskRequest
    │
    ▼
ChatCompletionRequest { Model, Messages = [system?, user], Stream = true }
    │
    ▼
ILlmSdkClient.CreateChatCompletionStreamAsync()
    │
    ▼
IAsyncEnumerable<ChatCompletionChunk>
    │
    ├── chunk.Choices[0].Delta.Content → write to stdout
    └── [DONE] → done
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| AskRequest | Prompt must be non-empty | System.CommandLine enforces (required argument) |
| CreateResponseRequest | Model resolved by LlmSdkClient | LlmSdkException if no model and no default |
| ChatCompletionRequest | Model resolved by LlmSdkClient | LlmSdkException if no model and no default |
