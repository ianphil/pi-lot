# Plan: CopilotLlmClient SDK Surface

## Summary

Add a `CopilotLlmClient` hero client to the CopilotLlm library that wraps existing proxy services with typed results, convenience overloads, typed streaming events, and configuration. Separate SDK and proxy concerns using namespaces within the same NuGet package.

## Architecture

```
SDK consumers                          Proxy hosts (llm-svc)
    │                                      │
    ▼                                      ▼
CopilotLlmClient                     IResponsesService
(CopilotLlm.Client)                  IChatCompletionsService
    │                                 (CopilotLlm.Proxy)
    │  deserializes JSON                   │
    │  throws typed exceptions             │  raw HTTP passthrough
    │                                      │
    └────────────┬─────────────────────────┘
                 ▼
         Shared core engine
      (model resolution, translation,
       auth, upstream HTTP adapter)
      (CopilotLlm.Core.Models,
       CopilotLlm.Core.Services)
```

## Detailed Architecture

```
AddCopilotLlm(options => ...)
    │
    ├─ Registers CopilotLlmOptions (singleton)
    ├─ Applies HttpTimeout to HttpClient
    ├─ Registers all existing services (unchanged)
    └─ Registers CopilotLlmClient (singleton)
           │
           ├─ IResponsesService ──► CreateResponseAsync() ──► Response
           │                    ──► CreateResponseStreamAsync() ──► IAsyncEnumerable<ResponseStreamEvent>
           │
           ├─ IChatCompletionsService ──► CreateChatCompletionAsync() ──► ChatCompletionResponse
           │                          ──► CreateChatCompletionStreamAsync() ──► IAsyncEnumerable<ChatCompletionChunk>
           │
           └─ ModelListService ──► ListModelsAsync() ──► IReadOnlyList<OpenAIModelInfo>
```

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| `CopilotLlmOptions` | Configuration bag (DefaultModel, HttpTimeout) | `AddCopilotLlm()` |
| `CopilotLlmClient` | Hero client — typed results, convenience overloads | `IResponsesService`, `IChatCompletionsService`, `ModelListService` |
| `ResponseStreamEvent` | Typed discriminated union for SSE events | `ResponseHttpResult.Chunks` parsing |
| `ResponseExtensions` | Extension methods (`GetOutputText()`) | `Response` model |
| `CopilotLlmException` | Typed exception hierarchy | Error status codes from services |

### Data Flow: Non-Streaming Request

```
client.CreateResponseAsync("gpt-5.4-mini", "Hello!")
    │
    ▼
Build CreateResponseRequest { Model, Input = JsonElement from string }
    │
    ▼
responsesService.CreateAsync(request)
    │
    ▼
ResponseHttpResult { Body = "{...}", StatusCode = 200 }
    │
    ├─ StatusCode >= 400 → Parse error JSON → throw CopilotLlmException
    │
    └─ StatusCode 2xx → Deserialize Body → return Response
```

### Data Flow: Streaming Request

```
client.CreateResponseStreamAsync("gpt-5.4-mini", "Hello!")
    │
    ▼
Build CreateResponseRequest { Model, Input, Stream = true }
    │
    ▼
responsesService.CreateAsync(request)
    │
    ▼
ResponseHttpResult { Chunks = IAsyncEnumerable<string> }
    │
    ▼
Parse each SSE chunk:
  "event: response.output_text.delta\ndata: {...}\n\n"
    │
    ▼
yield ResponseStreamEvent.TextDelta { Text = "Hello", ... }
```

## File Structure

```
CopilotLlm/
├── Core/
│   ├── Models/
│   │   ├── ResponseStreamEvent.cs          # NEW: typed stream event hierarchy
│   │   ├── CopilotLlmException.cs          # NEW: exception hierarchy
│   │   └── ... (existing models unchanged)
│   ├── Ports/
│   │   └── ... (existing, namespace → CopilotLlm.Proxy)
│   └── Services/
│       ├── CopilotLlmClient.cs             # NEW: hero client
│       ├── ResponseExtensions.cs           # NEW: extension methods
│       └── ... (existing services unchanged)
├── CopilotLlmOptions.cs                    # NEW: configuration
├── ServiceCollectionExtensions.cs          # MODIFY: add overload, register client
└── CopilotLlm.csproj                      # MODIFY: bump version to 0.3.0
```

## Critical: SSE Event Parsing

**Problem**: `ResponseHttpResult.Chunks` yields raw SSE strings (`"event: response.output_text.delta\ndata: {...}\n\n"`). `CopilotLlmClient` must parse these into typed `ResponseStreamEvent` objects without duplicating the serialization logic in `ResponseSseSerializer`.

**Solution**: Parse SSE lines (split on `event:` / `data:`) and deserialize the JSON payload based on the event type string. This is the inverse of `ResponseSseSerializer.SerializeEvent()`. The parser is a new static method on `ResponseStreamEvent` or a small internal helper.

## Implementation Phases

| Phase | Name | Scope |
|-------|------|-------|
| 1 | Options & DI | `CopilotLlmOptions`, `AddCopilotLlm(Action<>)` overload |
| 2 | Extension Methods | `ResponseExtensions.GetOutputText()`, `ChatCompletionExtensions.GetMessageText()` |
| 3 | Exceptions | `CopilotLlmException` hierarchy, error JSON parsing |
| 4 | Client (Non-Streaming) | `CopilotLlmClient` with request-object and convenience overloads |
| 5 | Client (Streaming) | `ResponseStreamEvent` hierarchy, streaming methods |
| 6 | Namespace Reorganization | Move types to `CopilotLlm.Client` / `CopilotLlm.Proxy`, update all usings |

Details in tasks.md.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Client wraps services | Delegate to `IResponsesService` etc. | No engine duplication, additive layer |
| Extension methods not instance methods | `ResponseExtensions.GetOutputText()` | CONTRIBUTING.md: "Models are plain DTOs, no behavior" |
| `Response` not wrapper type | Return existing `Response` directly | Issue #4 correction: no unnecessary `ResponseResult` indirection |
| Chat streaming reuses `ChatCompletionChunk` | No new event hierarchy for chat | Existing type is well-defined, matches upstream API |
| Namespace separation not `internal` | Public namespaces, no `InternalsVisibleTo` | Issue #4 Q6 correction: don't hide types artificially |
| Single NuGet package | Namespaces are not packages | Simpler versioning and publishing |

## Files to Modify

| File | Change |
|------|--------|
| `CopilotLlm/ServiceCollectionExtensions.cs` | Add options overload, register `CopilotLlmClient` |
| `CopilotLlm/CopilotLlm.csproj` | Bump version to 0.3.0 |
| `CopilotLlm/Core/Ports/*.cs` | Update namespace to `CopilotLlm.Proxy` |
| `CopilotLlm/Core/Services/*.cs` | Update `using` for new namespaces |
| `CopilotLlm/Infrastructure/*.cs` | Update `using` for new namespaces |
| `Program.cs` | Update `using` statements |
| `Worker.cs` | Update `using` statements |
| `CopilotLlm.Tests/**/*.cs` | Update `using` statements |
| `llm-svc.Tests/**/*.cs` | Update `using` statements |

## New Files

| File | Purpose |
|------|---------|
| `CopilotLlm/CopilotLlmOptions.cs` | Configuration class |
| `CopilotLlm/Core/Services/CopilotLlmClient.cs` | Hero client |
| `CopilotLlm/Core/Services/ResponseExtensions.cs` | Extension methods for Response |
| `CopilotLlm/Core/Services/ChatCompletionExtensions.cs` | Extension methods for ChatCompletionResponse |
| `CopilotLlm/Core/Models/ResponseStreamEvent.cs` | Typed stream event hierarchy |
| `CopilotLlm/Core/Models/CopilotLlmException.cs` | Exception hierarchy |
| `CopilotLlm.Tests/Unit/CopilotLlmOptionsTests.cs` | Options tests |
| `CopilotLlm.Tests/Unit/CopilotLlmClientTests.cs` | Client tests |
| `CopilotLlm.Tests/Unit/ResponseExtensionsTests.cs` | Extension method tests |
| `CopilotLlm.Tests/Unit/ResponseStreamEventTests.cs` | Stream event parsing tests |
| `CopilotLlm.Tests/Unit/CopilotLlmExceptionTests.cs` | Exception tests |

## Verification

1. `mise x dotnet@10.0.201 -- dotnet test CopilotLlm.Tests/CopilotLlm.Tests.csproj --no-restore`
2. `mise x dotnet@10.0.201 -- dotnet test llm-svc.Tests/llm-svc.Tests.csproj --filter "Category!=Smoke" --no-restore`
3. `mise x dotnet@10.0.201 -- dotnet build llm-svc.sln --no-restore` (full solution builds)

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Namespace move breaks builds | Phase 6 is atomic — update all files in one pass, verify build |
| SSE parsing misses event types | Test against `ResponseSseSerializer` output for all 15+ event types |
| Options interfere with existing DI | Parameterless overload calls options overload with defaults |

## Limitations (MVP)

1. No `CreateResponseOptions` convenience bag — use full `CreateResponseRequest` for advanced options
2. No `BaseUrl` configuration — always uses Copilot API
3. No retry/resilience policies beyond existing 401 retry
4. No manual construction of `CopilotLlmClient` without DI

## References

- [Issue #4: SDK API Surface Research](https://github.com/ianphil/copilot-llm-svc/issues/4)
- [Azure SDK General Design Guidelines](https://azure.github.io/azure-sdk/general_design.html)
- [Google AIP-4232: Flattened method signatures](https://google.aip.dev/client-libraries/4232)
