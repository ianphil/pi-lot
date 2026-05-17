# Raw APIs

Use raw APIs when you need OpenAI-shaped DTOs or exact wire behavior. Use the
portable context API for application logic that should work across Responses and
Chat Completions.

## Responses

```csharp
using System.Text.Json;
using LlmSdk.Core.Models;

var response = await client.CreateResponseAsync(new CreateResponseRequest
{
    Model = "gpt-5.4-mini",
    Input = JsonSerializer.SerializeToElement("Write a haiku.", JsonDefaults.Web),
    MaxOutputTokens = 200,
});

Console.WriteLine(response.GetOutputText());
```

`CreateResponseStreamAsync` yields `ResponseStreamEvent` records, including
response lifecycle events, text deltas, reasoning events, function-call
argument deltas, errors, and unknown events.

## Chat Completions

```csharp
var chat = await client.CreateChatCompletionAsync(new ChatCompletionRequest
{
    Model = "gpt-5-mini",
    Messages =
    [
        new ChatMessage { Role = "user", Content = "Write a haiku." },
    ],
});

Console.WriteLine(chat.GetMessageText());
```

`CreateChatCompletionStreamAsync` yields raw `ChatCompletionChunk` values.

## Model discovery

`ListModelsAsync` and `GetModelAsync` return `ModelInfo`, including advertised
endpoints, capabilities, token limits, pricing, and derived helpers such as
`SupportsVision`, `SupportsReasoning`, and `SupportsResponses`.
