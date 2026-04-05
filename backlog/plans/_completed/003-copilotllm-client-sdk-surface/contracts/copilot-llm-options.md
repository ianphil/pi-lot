# Contract: CopilotLlmOptions

## Purpose

Configuration for the CopilotLlm library. Applied during DI registration via `AddCopilotLlm(Action<CopilotLlmOptions>)`.

## Public API

```csharp
namespace CopilotLlm.Client;

public sealed class CopilotLlmOptions
{
    /// Default model used when callers omit the model parameter.
    /// null = no default (model is required on every request).
    public string? DefaultModel { get; set; }

    /// HTTP timeout for upstream requests. Default: 120 seconds.
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(120);
}
```

## DI Registration

```csharp
namespace CopilotLlm;

public static class ServiceCollectionExtensions
{
    /// Existing — preserved. Uses default options.
    public static IServiceCollection AddCopilotLlm(
        this IServiceCollection services);

    /// New overload — configure options.
    public static IServiceCollection AddCopilotLlm(
        this IServiceCollection services,
        Action<CopilotLlmOptions> configure);
}
```

## Behavior

### HttpTimeout

- Applied to `HttpClient.Timeout` during service registration
- Affects all upstream HTTP calls (model listing, responses, chat completions)
- Default 120s matches current implicit behavior

### DefaultModel

- Used by `CopilotLlmClient` when request's model property is null
- Does NOT affect `IResponsesService` or `IChatCompletionsService` directly (they validate model themselves)
- If both request model and default model are null, `CopilotLlmClient` throws `ArgumentException`

## Validation

| Field | Rule | Error |
|-------|------|-------|
| HttpTimeout | Must be > TimeSpan.Zero | `ArgumentOutOfRangeException` during registration |
| DefaultModel | If set, must be non-empty/non-whitespace | `ArgumentException` during registration |
