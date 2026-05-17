# Context API

The context API is the portable SDK surface for application code. It normalizes
Responses and Chat Completions into the same message, content, tool, usage, and
diagnostic types.

## Model

| Type | Purpose |
|---|---|
| `Context` | System prompt, ordered messages, and available tools |
| `UserMessage` | User content blocks |
| `AssistantMessage` | Model output, stop reason, usage, error, diagnostics |
| `ToolMessage` | Local tool result returned to the model |
| `TextContent` | Plain text |
| `ImageContent` | Base64 image input |
| `ThinkingContent` | Reasoning summary or redacted thinking metadata |
| `ToolCallContent` | Model-requested tool call |
| `ToolResultContent` | Tool output embedded as content |

## Options

`CompletionOptions` controls per-call behavior:

| Option | Notes |
|---|---|
| `Model` | Uses `LlmSdkOptions.DefaultModel` when omitted |
| `PreferredApi` | `Responses` by default; set `ChatCompletions` for chat-shaped routing |
| `AbortMode` | `ReturnPartial` by default for interrupted streams |
| `MaxOutputTokens`, `Temperature`, `TopP` | Generation controls forwarded when supported |
| `ToolChoice` | `Auto`, `None`, `Required`, or a named function |
| `Headers` | Extra upstream headers; `Authorization` cannot be overwritten |
| `RequestId`, `CorrelationId`, `Metadata` | Request tracing and SDK-local metadata |
| `TimeoutMs`, `MaxRetries`, `MaxRetryDelayMs` | Per-call transport controls |
| `Cache`, `SessionId` | Advisory prompt-cache/session affinity |
| `Thinking` | Requested reasoning effort, clamped to model support |
| `OnPayload`, `OnResponse` | Inspection hooks for payloads and response metadata |

## Stop reasons

`AssistantMessage.StopReason` is one of `Stop`, `Length`, `ToolUse`,
`ContentFilter`, `Aborted`, or `Error`. When `AbortMode.ReturnPartial` is used,
late cancellation and stream failures return an assistant message with partial
content instead of throwing after output has started.
