# Thinking and Reasoning

Set `CompletionOptions.Thinking` to request a reasoning effort:

```csharp
var message = await client.CompleteAsync(context, new CompletionOptions
{
    Model = "gpt-5.4",
    Thinking = ThinkingLevel.High,
});
```

Supported levels are `Minimal`, `Low`, `Medium`, `High`, and `XHigh`. The SDK
uses `ModelInfo.SupportedThinkingLevels` to clamp unsupported requested levels
down to the nearest model-supported level. If the requested level is adjusted,
the assistant message includes a `thinking_clamped` diagnostic.

Reasoning content returned by Copilot is represented as `ThinkingContent`.
Redacted reasoning signatures are preserved so callers can include prior
assistant messages in later context turns without losing continuity metadata.

Chat Completions does not preserve the same reasoning shape as Responses. Use
Responses-backed context calls when redacted thinking continuity matters.
