---
title: "CopilotLlmClient — public SDK surface"
status: open
priority: high
created: 2026-04-04
related: "#4"
---

# CopilotLlmClient — Public SDK Surface

## Summary

Build the public `CopilotLlmClient` hero client that wraps existing proxy services with typed results, convenience overloads, and exceptions — giving SDK consumers a clean API while keeping the proxy surface available in a separate namespace.

## Motivation

The library currently exposes proxy-shaped interfaces (`IResponsesService`, `IChatCompletionsService`) returning HTTP status codes and raw JSON. SDK consumers shouldn't parse JSON or check status codes. They need typed results, convenience overloads for common scenarios, and exceptions on failure.

## Proposal

### Goals

- Single `CopilotLlmClient` entry point with typed results and streaming
- Convenience overloads for hero path (`model + string`)
- `CopilotLlmOptions` for configuration (`DefaultModel`, `HttpTimeout`)
- Extension methods for common operations (`GetOutputText()`)
- Namespace separation: `CopilotLlm.Client` (SDK) / `CopilotLlm.Proxy` (transport)

### Non-Goals

- Breaking changes to existing proxy API
- Model name constants (deferred — model catalog too volatile)
- Retry policies beyond existing 401 retry
- Multiple NuGet packages

## Design

Two public namespaces in one package:

```
CopilotLlm.Client     → CopilotLlmClient, CopilotLlmOptions, ResponseStreamEvent
CopilotLlm.Proxy      → IResponsesService, IChatCompletionsService, ResponseHttpResult
CopilotLlm.Core.Models → Shared DTOs (Response, ChatCompletionResponse, etc.)
```

`CopilotLlmClient` wraps the existing services internally — deserializes raw JSON into typed models, translates HTTP errors to exceptions. Both surfaces share the same core engine (model resolution, translation, auth, upstream HTTP).

Streaming uses typed discriminated events (`TextDelta`, `ToolCallStart`, `Done`) with an accumulating partial message. Extension methods (`ResponseExtensions.GetOutputText()`) keep DTOs pure per project convention.

## Tasks

- [ ] Create `CopilotLlmOptions` with `DefaultModel` and `HttpTimeout`
- [ ] Add `AddCopilotLlm(Action<CopilotLlmOptions>)` overload (existing parameterless stays)
- [ ] Define `ResponseStreamEvent` typed event hierarchy in `Core/Models/`
- [ ] Build `CopilotLlmClient` with request-object methods (Responses, Chat, Models)
- [ ] Add convenience overloads (`model + string` for hero path)
- [ ] Add `ResponseExtensions.GetOutputText()` extension method
- [ ] Move existing proxy types into `CopilotLlm.Proxy` namespace
