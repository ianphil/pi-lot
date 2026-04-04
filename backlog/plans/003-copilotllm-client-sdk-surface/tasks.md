# CopilotLlmClient SDK Surface Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

## Dependencies

```
Phase 1 (Options & DI)
    │
    ├──► Phase 2 (Extension Methods) — independent, no deps on Phase 1
    │
    ├──► Phase 3 (Exceptions) — independent, no deps on Phase 1
    │
    └──► Phase 4 (Client Non-Streaming) — depends on Phase 1, 2, 3
              │
              └──► Phase 5 (Client Streaming) — depends on Phase 4
                        │
                        └──► Phase 6 (Namespace Reorganization) — depends on all
```

---

## Phase 1: Options & DI

### Configuration

- [x] T001 [TEST] `CopilotLlmOptions` has `DefaultModel` and `HttpTimeout` with correct defaults
- [x] T002 [IMPL] Create `CopilotLlmOptions` class
- [x] T003 [TEST] `AddCopilotLlm(Action<CopilotLlmOptions>)` registers options and applies `HttpTimeout` to HttpClient
- [x] T004 [IMPL] Add options overload to `ServiceCollectionExtensions`
- [x] T005 [TEST] Parameterless `AddCopilotLlm()` still works and uses default options
- [x] T006 [IMPL] Wire parameterless overload to delegate to options overload
- [x] T007 [TEST] Options validation rejects invalid `HttpTimeout` and empty `DefaultModel`
- [x] T008 [IMPL] Add validation in registration

---

## Phase 2: Extension Methods

### Response Extensions

- [x] T009 [TEST] `GetOutputText()` returns text from first message output item
- [x] T010 [TEST] `GetOutputText()` returns null when no message items exist
- [x] T011 [TEST] `GetOutputText()` returns null when message has no text content parts
- [x] T012 [IMPL] Create `ResponseExtensions.GetOutputText()`

### ChatCompletion Extensions

- [x] T013 [TEST] `GetMessageText()` returns content from first choice
- [x] T014 [TEST] `GetMessageText()` returns null when no choices exist
- [x] T015 [IMPL] Create `ChatCompletionExtensions.GetMessageText()`

---

## Phase 3: Exceptions

### Exception Hierarchy

- [x] T016 [TEST] `CopilotLlmException` carries `ErrorCode`, `ErrorType`, `Param`, `StatusCode`
- [x] T017 [IMPL] Create `CopilotLlmException` base class
- [x] T018 [TEST] `ModelNotFoundException` is a `CopilotLlmException`
- [x] T019 [TEST] `AuthenticationException` is a `CopilotLlmException`
- [x] T020 [TEST] `RateLimitException` carries `RetryAfter` TimeSpan
- [x] T021 [IMPL] Create `ModelNotFoundException`, `AuthenticationException`, `RateLimitException`

### Error Parsing

- [x] T022 [TEST] Error JSON with `model_not_found` code maps to `ModelNotFoundException`
- [x] T023 [TEST] 401 status maps to `AuthenticationException`
- [x] T024 [TEST] 429 status maps to `RateLimitException` with RetryAfter
- [x] T025 [TEST] Unknown error maps to base `CopilotLlmException`
- [x] T026 [IMPL] Create `CopilotLlmExceptionFactory` that parses error JSON and returns correct subtype

---

## Phase 4: Client (Non-Streaming)

### Client Registration

- [x] T027 [TEST] `CopilotLlmClient` is resolvable from DI after `AddCopilotLlm()`
- [x] T028 [IMPL] Register `CopilotLlmClient` in `ServiceCollectionExtensions`

### Responses API — Request Object

- [x] T029 [TEST] `CreateResponseAsync(CreateResponseRequest)` returns deserialized `Response` on success
- [x] T030 [TEST] `CreateResponseAsync(CreateResponseRequest)` throws `CopilotLlmException` on error status
- [x] T031 [IMPL] Implement `CreateResponseAsync(CreateResponseRequest)`

### Responses API — Convenience

- [x] T032 [TEST] `CreateResponseAsync(model, input)` builds correct `CreateResponseRequest` and returns `Response`
- [x] T033 [TEST] `CreateResponseAsync(null, input)` uses `DefaultModel` from options
- [x] T034 [TEST] `CreateResponseAsync(null, input)` throws `ArgumentException` when no default model set
- [x] T035 [IMPL] Implement `CreateResponseAsync(string, string)`

### Chat Completions API — Request Object

- [x] T036 [TEST] `CreateChatCompletionAsync(ChatCompletionRequest)` returns deserialized `ChatCompletionResponse`
- [x] T037 [TEST] `CreateChatCompletionAsync(ChatCompletionRequest)` throws `CopilotLlmException` on error
- [x] T038 [IMPL] Implement `CreateChatCompletionAsync(ChatCompletionRequest)`

### Chat Completions API — Convenience

- [x] T039 [TEST] `CreateChatCompletionAsync(model, message)` builds correct request and returns response
- [x] T040 [IMPL] Implement `CreateChatCompletionAsync(string, string)`

### Models API

- [x] T041 [TEST] `ListModelsAsync()` returns model list from `ModelListService`
- [x] T042 [IMPL] Implement `ListModelsAsync()`

---

## Phase 5: Client (Streaming)

### ResponseStreamEvent Hierarchy

- [ ] T043 [TEST] `ResponseStreamEvent.Parse` parses `response.output_text.delta` SSE chunk into `OutputTextDelta`
- [ ] T044 [TEST] `ResponseStreamEvent.Parse` parses `response.completed` SSE chunk into `ResponseCompleted`
- [ ] T045 [TEST] `ResponseStreamEvent.Parse` parses `response.function_call_arguments.delta` into `FunctionCallArgumentsDelta`
- [ ] T046 [TEST] `ResponseStreamEvent.Parse` handles `[DONE]` sentinel
- [ ] T047 [TEST] `ResponseStreamEvent.Parse` parses all lifecycle events (`created`, `in_progress`, `failed`, `incomplete`)
- [ ] T048 [TEST] `ResponseStreamEvent.Parse` parses content part events (`added`, `done`)
- [ ] T049 [TEST] `ResponseStreamEvent.Parse` parses output item events (`added`, `done`)
- [ ] T050 [IMPL] Create `ResponseStreamEvent` hierarchy and `Parse` method

### Streaming Responses API

- [ ] T051 [TEST] `CreateResponseStreamAsync(CreateResponseRequest)` yields parsed `ResponseStreamEvent` objects
- [ ] T052 [TEST] `CreateResponseStreamAsync` throws on error status before streaming
- [ ] T053 [IMPL] Implement `CreateResponseStreamAsync(CreateResponseRequest)`
- [ ] T054 [TEST] `CreateResponseStreamAsync(model, input)` convenience overload works
- [ ] T055 [IMPL] Implement `CreateResponseStreamAsync(string, string)`

### Streaming Chat Completions API

- [ ] T056 [TEST] `CreateChatCompletionStreamAsync(ChatCompletionRequest)` yields `ChatCompletionChunk` objects
- [ ] T057 [IMPL] Implement `CreateChatCompletionStreamAsync(ChatCompletionRequest)`
- [ ] T058 [TEST] `CreateChatCompletionStreamAsync(model, message)` convenience overload works
- [ ] T059 [IMPL] Implement `CreateChatCompletionStreamAsync(string, string)`

---

## Phase 6: Namespace Reorganization

### Namespace Moves

- [ ] T060 [TEST] Verify solution builds after namespace changes (build test)
- [ ] T061 [IMPL] Move `Core/Ports/` types to `CopilotLlm.Proxy` namespace
- [ ] T062 [IMPL] Move new client types to `CopilotLlm.Client` namespace
- [ ] T063 [IMPL] Update all `using` statements in `CopilotLlm/` source files
- [ ] T064 [IMPL] Update all `using` statements in `Program.cs`, `Worker.cs`
- [ ] T065 [IMPL] Update all `using` statements in `CopilotLlm.Tests/`
- [ ] T066 [IMPL] Update all `using` statements in `llm-svc.Tests/`
- [ ] T067 [IMPL] Bump `CopilotLlm.csproj` version to `0.3.0`

### Final Validation

- [ ] T068 [TEST] Full solution build succeeds
- [ ] T069 [TEST] All `CopilotLlm.Tests` pass
- [ ] T070 [TEST] All `llm-svc.Tests` (Category!=Smoke) pass

---

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Options & DI | T001–T008 | 4 | 4 |
| Phase 2: Extension Methods | T009–T015 | 5 | 2 |
| Phase 3: Exceptions | T016–T026 | 8 | 3 |
| Phase 4: Client (Non-Streaming) | T027–T042 | 9 | 7 |
| Phase 5: Client (Streaming) | T043–T059 | 11 | 6 |
| Phase 6: Namespace Reorg | T060–T070 | 4 | 7 |
| **Total** | **70** | **41** | **29** |

## Final Validation

After all implementation phases are complete:

- [ ] `mise x dotnet@10.0.201 -- dotnet build llm-svc.sln --no-restore` passes
- [ ] `mise x dotnet@10.0.201 -- dotnet test CopilotLlm.Tests/CopilotLlm.Tests.csproj --no-restore` passes
- [ ] `mise x dotnet@10.0.201 -- dotnet test llm-svc.Tests/llm-svc.Tests.csproj --filter "Category!=Smoke" --no-restore` passes
