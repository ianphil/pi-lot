# Specification: CopilotLlmClient SDK Surface

## Overview

### Problem Statement

The CopilotLlm library exposes proxy-shaped interfaces (`IResponsesService`, `IChatCompletionsService`) returning HTTP status codes, raw JSON bodies, and untyped SSE string chunks. SDK consumers must deserialize JSON, check status codes, parse SSE events, and construct `JsonElement` inputs for simple string prompts. This friction makes the library unusable as a standalone SDK — it only works well inside a proxy host.

### Solution Summary

Add a `CopilotLlmClient` hero client that wraps existing services with typed results, convenience overloads for common scenarios, typed streaming events, and configuration via `CopilotLlmOptions`. Separate the SDK surface (`CopilotLlm.Client`) from the proxy surface (`CopilotLlm.Proxy`) using namespaces within the same NuGet package.

### Business Value

| Benefit | Impact |
|---------|--------|
| 3-line hello world | SDK adoption — consumers can call Copilot with minimal code |
| Typed results | No JSON parsing — `result.GetOutputText()` for the 90% case |
| Typed streaming | Pattern-match on events instead of parsing SSE text |
| Configuration | Consumers can set timeouts, default models |
| Namespace clarity | Self-documenting: `using CopilotLlm.Client` vs `using CopilotLlm.Proxy` |

## User Stories

### SDK Consumer

**As a .NET developer**, I want to call Copilot LLMs with a single client and get typed results, so that I don't need to understand HTTP plumbing or JSON serialization.

**Acceptance Criteria:**
- I can resolve `CopilotLlmClient` from DI
- I can call `CreateResponseAsync("gpt-5.4-mini", "Hello!")` and get a typed `Response`
- I can call `GetOutputText()` on the response to extract the first text output
- I can stream with `CreateResponseStreamAsync` and get typed events
- Errors throw typed exceptions, not return status codes

### Proxy Host Developer

**As the llm-svc maintainer**, I want the proxy interfaces to remain available and unchanged, so that endpoint handlers continue working without modification.

**Acceptance Criteria:**
- `IResponsesService`, `IChatCompletionsService` remain public and functional
- `ResponseHttpResult` continues to work for HTTP response writing
- `Program.cs` requires only `using` statement updates after namespace move
- No behavioral changes to existing proxy functionality

### Library Configurator

**As a host application developer**, I want to configure the library's default model and HTTP timeout, so that I can tune behavior without modifying request code.

**Acceptance Criteria:**
- `AddCopilotLlm()` parameterless overload still works
- `AddCopilotLlm(o => o.DefaultModel = "gpt-5.4-mini")` sets defaults
- `HttpTimeout` is applied to the underlying `HttpClient`
- `DefaultModel` is used when request omits model

## Functional Requirements

### FR-1: Configuration

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | `CopilotLlmOptions` class with `DefaultModel` (string?) and `HttpTimeout` (TimeSpan, default 120s) |
| FR-1.2 | `AddCopilotLlm(Action<CopilotLlmOptions>)` overload registers options and applies them |
| FR-1.3 | Parameterless `AddCopilotLlm()` continues to work with default options |
| FR-1.4 | `HttpTimeout` is applied to the `HttpClient` via `HttpClient.Timeout` |

### FR-2: Hero Client

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | `CopilotLlmClient` registered as singleton via `AddCopilotLlm()` |
| FR-2.2 | Request-object overload: `CreateResponseAsync(CreateResponseRequest, CancellationToken)` returns `Response` |
| FR-2.3 | Convenience overload: `CreateResponseAsync(string model, string input, CancellationToken)` returns `Response` |
| FR-2.4 | Request-object overload: `CreateChatCompletionAsync(ChatCompletionRequest, CancellationToken)` returns `ChatCompletionResponse` |
| FR-2.5 | Convenience overload: `CreateChatCompletionAsync(string model, string message, CancellationToken)` returns `ChatCompletionResponse` |
| FR-2.6 | `ListModelsAsync(CancellationToken)` returns `IReadOnlyList<OpenAIModelInfo>` |
| FR-2.7 | Default model from `CopilotLlmOptions` used when request model is null |

### FR-3: Streaming

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | `CreateResponseStreamAsync(CreateResponseRequest, CancellationToken)` returns `IAsyncEnumerable<ResponseStreamEvent>` |
| FR-3.2 | `CreateResponseStreamAsync(string model, string input, CancellationToken)` convenience overload |
| FR-3.3 | `CreateChatCompletionStreamAsync(ChatCompletionRequest, CancellationToken)` returns `IAsyncEnumerable<ChatCompletionChunk>` |
| FR-3.4 | `CreateChatCompletionStreamAsync(string model, string message, CancellationToken)` convenience overload |
| FR-3.5 | `ResponseStreamEvent` is a discriminated union with typed variants for each SSE event type |

### FR-4: Extension Methods

| Requirement | Description |
|-------------|-------------|
| FR-4.1 | `Response.GetOutputText()` returns first text output or null |
| FR-4.2 | `ChatCompletionResponse.GetMessageText()` returns first choice message content or null |

### FR-5: Error Handling

| Requirement | Description |
|-------------|-------------|
| FR-5.1 | Non-2xx responses from proxy services throw `CopilotLlmException` |
| FR-5.2 | 404 / model_not_found throws `ModelNotFoundException` |
| FR-5.3 | 401 / auth errors throw `AuthenticationException` |
| FR-5.4 | 429 throws `RateLimitException` with `RetryAfter` if available |
| FR-5.5 | Exception includes `ErrorCode`, `ErrorType`, `Message`, `Param` from error JSON |

### FR-6: Namespace Organization

| Requirement | Description |
|-------------|-------------|
| FR-6.1 | New client types live in `CopilotLlm.Client` namespace |
| FR-6.2 | Existing proxy types move to `CopilotLlm.Proxy` namespace |
| FR-6.3 | Shared DTOs remain in `CopilotLlm.Core.Models` |
| FR-6.4 | All `using` statements across `llm-svc`, test projects updated |

## Scope

### In Scope

- `CopilotLlmClient` with typed non-streaming and streaming methods
- `CopilotLlmOptions` with `DefaultModel` and `HttpTimeout`
- `ResponseStreamEvent` discriminated union for Responses API streaming
- Extension methods for `Response` and `ChatCompletionResponse`
- `CopilotLlmException` hierarchy
- Namespace reorganization to `CopilotLlm.Client` / `CopilotLlm.Proxy`
- Unit tests for all new types
- Update `llm-svc` `Program.cs` usings

### Out of Scope

- Model name constants (deferred — catalog too volatile)
- Retry policies beyond existing 401 retry
- Multiple NuGet packages (single package, namespace separation only)
- Chat Completions typed stream events (reuse existing `ChatCompletionChunk`)
- `CopilotLlmClient` without DI (manual construction not supported in v0.3)

### Future Considerations

- `BaseUrl` option for custom Copilot API endpoints
- Retry/resilience policy configuration
- `CreateResponseOptions` convenience bag (instructions, temperature, etc.)
- Model constants auto-generated from live `/models` endpoint

## Assumptions

1. Library is at v0.2.0 (pre-1.0) — breaking namespace changes are acceptable
2. `llm-svc` is the only consumer of proxy interfaces — updating usings is trivial
3. `CopilotLlmClient` can depend on the existing internal services via DI
4. Chat Completions streaming can reuse existing `ChatCompletionChunk` type without a new event hierarchy

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Namespace move breaks external consumers | Low | Medium | Pre-1.0 version, no known external consumers |
| SSE event parsing misses edge cases | Medium | Medium | Test against existing `ResponseSseSerializer` output |
| Options configuration interferes with existing DI | Low | High | Parameterless overload delegates to options overload with defaults |
| `CopilotLlmClient` adds unnecessary indirection | Low | Low | Thin wrapper — delegates directly to services, no new logic |
