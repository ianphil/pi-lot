# Errors and Retries

The SDK maps common upstream failures to typed exceptions:

| Exception | Typical status | Meaning |
|---|---:|---|
| `ModelNotFoundException` | 404 | Requested model is unavailable |
| `AuthenticationException` | 401 | Copilot credential is missing, expired, or invalid |
| `RateLimitException` | 429 | Upstream rate limit; inspect `RetryAfter` |
| `ContextOverflowException` | 400 | Request exceeds the model context window |
| `LlmSdkException` | varies | Other SDK or upstream error responses |

```csharp
try
{
    var message = await client.CompleteAsync(context, options);
}
catch (RateLimitException ex) when (ex.RetryAfter is { } delay)
{
    await Task.Delay(delay);
}
catch (LlmSdkException ex)
{
    Console.Error.WriteLine($"[{ex.StatusCode}] {ex.ErrorCode}: {ex.Message}");
}
```

Use `CompletionOptions.TimeoutMs`, `MaxRetries`, and `MaxRetryDelayMs` for
per-call transport controls. The SDK reloads Copilot credentials before requests
when needed and retries once on a 401 with a freshly loaded credential.

Inspection hooks are best-effort. `OnPayload` and `OnResponse` exceptions are
logged and do not fail the request.
