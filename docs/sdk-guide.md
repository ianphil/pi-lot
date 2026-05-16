# LlmSdk Guide

`LlmSdk` is a .NET library for accessing GitHub Copilot's LLM API. It handles
credential resolution, model discovery, request routing, and automatic
translation between the Responses and Chat Completions API surfaces.

For a higher-level tool-calling loop built on top of `LlmSdk`, see
`docs/agent-guide.md`.

## Installation

From [GitHub Packages](https://github.com/ianphil/copilot-llm-svc/packages):

```bash
dotnet add package LlmSdk --source https://nuget.pkg.github.com/ianphil/index.json
```

Or as a project reference within this repo (path is relative to your project):

```xml
<!-- from src/llm-svc/ or src/llm-cli/ -->
<ProjectReference Include="..\llm-sdk\llm-sdk.csproj" />

<!-- from tests/llm-sdk.Tests/ -->
<ProjectReference Include="..\..\src\llm-sdk\llm-sdk.csproj" />
```

## Quick start

```csharp
using LlmSdk;
using LlmSdk.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddLlmSdk();

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ILlmSdkClient>();

// Responses API
var response = await client.CreateResponseAsync("gpt-5.4-mini", "Hello!");
Console.WriteLine(response.GetOutputText());

// Chat Completions API
var chat = await client.CreateChatCompletionAsync("gpt-5-mini", "Hello!");
Console.WriteLine(chat.GetMessageText());
```

> **Important:** Call `services.AddLogging()` before `services.AddLlmSdk()`.
> The SDK's internal services depend on `ILogger` being registered.

---

## Configuration

### AddLlmSdk

`AddLlmSdk()` is the single DI entry point. It registers all SDK services
including credential stores, HTTP clients, translators, and the public client.

```csharp
// No-arg form — uses defaults
services.AddLlmSdk();

// Configure options
services.AddLlmSdk(options =>
{
    options.DefaultModel = "gpt-5.4-mini";
    options.HttpTimeout = TimeSpan.FromSeconds(60);
});
```

### LlmSdkOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultModel` | `string?` | `null` | Model to use when none is specified on a request. If null and no model is passed, the call throws `ArgumentException`. |
| `HttpTimeout` | `TimeSpan` | 120 seconds | Timeout for upstream HTTP requests. Must be greater than zero. |

---

## Responses API

### Non-streaming

```csharp
// Simple string input
var response = await client.CreateResponseAsync("gpt-5.4-mini", "What is 2+2?");
Console.WriteLine(response.GetOutputText());
```

```csharp
// Full request object
using System.Text.Json;
using LlmSdk.Core.Models;

var request = new CreateResponseRequest
{
    Model = "gpt-5.4-mini",
    Input = JsonSerializer.SerializeToElement("Explain monads", JsonDefaults.Web),
    Instructions = "Be concise.",
    Temperature = 0.7,
    MaxOutputTokens = 500,
};

var response = await client.CreateResponseAsync(request);
Console.WriteLine(response.GetOutputText());
```

### Streaming

```csharp
await foreach (var evt in client.CreateResponseStreamAsync("gpt-5.4-mini", "Write a haiku"))
{
    if (evt is OutputTextDeltaEvent delta)
    {
        Console.Write(delta.Delta);
    }
}

Console.WriteLine();
```

```csharp
// Full request with streaming
var request = new CreateResponseRequest
{
    Model = "gpt-5.4-mini",
    Input = JsonSerializer.SerializeToElement("Tell me a joke", JsonDefaults.Web),
};

await foreach (var evt in client.CreateResponseStreamAsync(request))
{
    switch (evt)
    {
        case OutputTextDeltaEvent delta:
            Console.Write(delta.Delta);
            break;
        case ResponseCompletedEvent completed:
            Console.WriteLine($"\n\nTokens: {completed.Response.Usage?.TotalTokens}");
            break;
        case ErrorEvent error:
            Console.Error.WriteLine($"Error: {error.Error.Message}");
            break;
    }
}
```

---

## Chat Completions API

### Non-streaming

```csharp
// Simple string input
var chat = await client.CreateChatCompletionAsync("gpt-5-mini", "What is 2+2?");
Console.WriteLine(chat.GetMessageText());
```

```csharp
// Full request object
var request = new ChatCompletionRequest
{
    Model = "claude-haiku-4.5",
    Messages =
    [
        new ChatMessage { Role = "system", Content = "Be concise." },
        new ChatMessage { Role = "user", Content = "Explain monads" },
    ],
    Temperature = 0.7,
    MaxCompletionTokens = 500,
};

var chat = await client.CreateChatCompletionAsync(request);
Console.WriteLine(chat.GetMessageText());
```

### Streaming

```csharp
await foreach (var chunk in client.CreateChatCompletionStreamAsync("gpt-5-mini", "Write a haiku"))
{
    var content = chunk.Choices?.FirstOrDefault()?.Delta?.Content;
    if (content is not null)
    {
        Console.Write(content);
    }
}

Console.WriteLine();
```

---

## Model discovery

```csharp
var models = await client.ListModelsAsync();

foreach (var model in models)
{
    Console.WriteLine($"{model.Id,-30} {model.Name}");
}
```

Each `ModelInfo` includes the model details reported by the upstream API,
including `SupportedEndpoints`, capabilities, and token limits. It also includes
SDK-supplied metadata such as `ProxySupportedEndpoints` and pricing when the SDK
has it.

```csharp
var modelInfo = await client.GetModelAsync("gpt-4o");

Console.WriteLine($"{modelInfo.DisplayName}: context={modelInfo.ContextWindow}");
Console.WriteLine($"Input price / 1M tokens: {modelInfo.Pricing?.InputPerMillionTokens}");
```

`ListModelsAsync()` and `GetModelAsync()` use the same `ModelInfo` shape.
Unknown models use conservative defaults: capability flags are false and token
limits/pricing are null unless upstream reports token limits.

For runs that return usage, pass the same model metadata to `UsageMath` to
estimate cost:

```csharp
var usage = response.Usage;
var cost = usage is null ? null : UsageMath.CalculateCost(usage, modelInfo);
```

---

## Error handling

The SDK throws typed exceptions that map to upstream error categories:

| Exception | Status code | When |
|---|---|---|
| `ModelNotFoundException` | 404 | Unknown model ID |
| `AuthenticationException` | 401 | Upstream returns 401 (expired or invalid token) |
| `RateLimitException` | 429 | Upstream rate limit; check `RetryAfter` |
| `LlmSdkException` | varies | All other errors |

```csharp
try
{
    var response = await client.CreateResponseAsync("nonexistent-model", "Hello");
}
catch (ModelNotFoundException ex)
{
    Console.Error.WriteLine($"Model not found: {ex.Message}");
}
catch (RateLimitException ex)
{
    Console.Error.WriteLine($"Rate limited. Retry after: {ex.RetryAfter}");
}
catch (AuthenticationException ex)
{
    Console.Error.WriteLine($"Auth failed: {ex.Message}");
}
catch (LlmSdkException ex)
{
    Console.Error.WriteLine($"[{ex.StatusCode}] {ex.ErrorCode}: {ex.Message}");
}
```

All exceptions expose:

| Property | Type | Description |
|---|---|---|
| `Message` | `string` | Human-readable error description |
| `StatusCode` | `int` | HTTP status code from upstream |
| `ErrorCode` | `string?` | Machine-readable code (e.g., `model_not_found`) |
| `ErrorType` | `string?` | Error category (e.g., `invalid_request_error`) |
| `Param` | `string?` | Which parameter caused the error |

---

## Streaming event types

`CreateResponseStreamAsync` yields `ResponseStreamEvent` records. Use pattern
matching to handle specific event types:

### Response lifecycle

| Event | Key properties | When |
|---|---|---|
| `ResponseCreatedEvent` | `Response` | Response object created |
| `ResponseInProgressEvent` | `Response` | Processing started |
| `ResponseCompletedEvent` | `Response` | Successful completion |
| `ResponseFailedEvent` | `Response` | Error during processing |
| `ResponseIncompleteEvent` | `Response` | Truncated (e.g., length limit) |
| `ResponseQueuedEvent` | `Response` | Queued for background processing |

### Content

| Event | Key properties | When |
|---|---|---|
| `OutputItemAddedEvent` | `Item`, `OutputIndex` | New output item begins |
| `OutputItemDoneEvent` | `Item`, `OutputIndex` | Output item complete |
| `ContentPartAddedEvent` | `Part`, `OutputIndex`, `ContentIndex` | Content part initialized |
| `ContentPartDoneEvent` | `Part`, `OutputIndex`, `ContentIndex` | Content part complete |
| `OutputTextDeltaEvent` | `Delta`, `OutputIndex`, `ContentIndex` | Text token received |
| `OutputTextDoneEvent` | `Text`, `OutputIndex`, `ContentIndex` | Content part text finalized |
| `OutputTextAnnotationAddedEvent` | `Annotation`, `AnnotationIndex` | Annotation on text output |

### Tool calling

| Event | Key properties | When |
|---|---|---|
| `FunctionCallArgumentsDeltaEvent` | `Delta`, `OutputIndex` | Tool call argument chunk |
| `FunctionCallArgumentsDoneEvent` | `Arguments`, `OutputIndex` | Tool call arguments finalized |

### Refusal

| Event | Key properties | When |
|---|---|---|
| `RefusalDeltaEvent` | `Delta` | Refusal text chunk |
| `RefusalDoneEvent` | `Refusal` | Refusal text finalized |

### Reasoning

| Event | Key properties | When |
|---|---|---|
| `ReasoningDeltaEvent` | `Delta` | Chain-of-thought chunk |
| `ReasoningDoneEvent` | `OutputIndex` | Reasoning complete |
| `ReasoningSummaryPartAddedEvent` | `Part`, `SummaryIndex` | Summary part initialized |
| `ReasoningSummaryPartDoneEvent` | `Part`, `SummaryIndex` | Summary part complete |
| `ReasoningSummaryDeltaEvent` | `Delta`, `SummaryIndex` | Summary text chunk |
| `ReasoningSummaryDoneEvent` | `Text`, `SummaryIndex` | Summary text finalized |

### Error and unknown

| Event | Key properties | When |
|---|---|---|
| `ErrorEvent` | `Error` | Stream error |
| `UnknownStreamEvent` | `EventName`, `RawData` | Unrecognized event type |

---

## Extension methods

### ResponseExtensions

```csharp
using LlmSdk.Client;

string? text = response.GetOutputText();
```

Extracts the first `output_text` from the first `message` item in the response.
Returns `null` if the response contains no text output.

### ChatCompletionExtensions

```csharp
using LlmSdk.Client;

string? text = chatResponse.GetMessageText();
```

Extracts the message content from the first choice. Handles both `string` and
`JsonElement`-valued content fields.

---

## Credential resolution

The SDK resolves Copilot credentials automatically in this order:

1. `COPILOT_TOKEN` environment variable (all platforms)
2. Windows Credential Manager entries created by Copilot CLI
3. Linux Secret Service (D-Bus) entries created by Copilot CLI

On Linux, the SDK reads `~/.copilot/config.json` to determine the
`last_logged_in_user` for account selection. The token itself comes from Secret
Service.

If no credential is found, the SDK operates in a degraded mode. Model discovery
returns an empty list, so request APIs typically fail with
`ModelNotFoundException` rather than `AuthenticationException`. If credentials
expire mid-session, failures may surface as transport exceptions
(`HttpRequestException`) before the SDK's typed exception mapping runs.

On platforms other than Windows and Linux (e.g., macOS), there is no built-in
credential store — use `COPILOT_TOKEN`. Headless and container environments
should also set `COPILOT_TOKEN` directly.

---

## Port interfaces

For advanced use cases (custom hosts, testing, or alternative implementations),
the SDK exposes port interfaces in `LlmSdk.Proxy`:

| Interface | Purpose |
|---|---|
| `IResponsesService` | Create Responses API requests |
| `IChatCompletionsService` | Create Chat Completions requests |
| `IModelProvider` | Fetch models, send raw upstream requests |
| `IAuthProvider` | Check auth state, load/validate credentials |
| `ICopilotCredentialStore` | Read credentials from a platform store |

These are the boundaries between the SDK's domain logic and its infrastructure.
`AddLlmSdk()` wires the default implementations. You can replace any of them
by registering your own implementation after calling `AddLlmSdk()`.

---

## Namespaces

| Namespace | Contents |
|---|---|
| `LlmSdk` | `ServiceCollectionExtensions` |
| `LlmSdk.Client` | `ILlmSdkClient`, `LlmSdkClient`, `LlmSdkOptions`, exceptions, extensions, stream events |
| `LlmSdk.Proxy` | Port interfaces (`IResponsesService`, `IModelProvider`, etc.) |
| `LlmSdk.Core.Models` | DTOs — `CreateResponseRequest`, `ChatCompletionRequest`, `Response`, etc. |
| `LlmSdk.Core.Services` | Internal services — translators, serializers, parsers |
| `LlmSdk.Infrastructure` | HTTP adapters, credential stores |
