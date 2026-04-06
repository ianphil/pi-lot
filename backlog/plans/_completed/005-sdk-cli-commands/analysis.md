# SDK CLI Commands Analysis

## Executive Summary

| Pattern | Integration Point |
|---------|-------------------|
| Delegate-based agent | New SdkAskAgent/SdkChatAgent wrapping ILlmSdkClient |
| System.CommandLine subcommand | `sdk-ask` and `sdk-chat` registered in Program.cs |
| AskRequest shared DTO | Reused for SDK commands (same flags) |
| IAsyncEnumerable streaming | SDK client streams ResponseStreamEvent / ChatCompletionChunk |
| DI via AddLlmSdk() | SDK agents build their own ServiceProvider in-process |

## Architecture Comparison

### Current Architecture

```
llm-cli (ask/chat)
    │
    ▼
OpenAI .NET SDK (ResponsesClient / ChatClient)
    │
    ▼
HTTP → localhost:5100 (llm-svc proxy)
    │
    ▼
Copilot upstream API
```

The CLI only exercises the proxy HTTP surface. The LlmSdk library is consumed
only by llm-svc (host) and unit tests.

### Target Architecture

```
llm-cli
    ├── ask / chat ──────────► OpenAI SDK ──► HTTP ──► llm-svc ──► Copilot API
    │
    └── sdk-ask / sdk-chat ──► ILlmSdkClient (in-process) ──► Copilot API
```

The CLI becomes a reference consumer for both surfaces, proving end-to-end
that the SDK library works from a real application.

## Pattern Mapping

### 1. Agent Construction (Delegate-Based)

**Current Implementation:**
`AskAgent.Create()` wraps `ResponsesClient` methods into delegates:
- `Func<CreateResponseOptions, CancellationToken, Task<ResponseResult>>`
- `Func<CreateResponseOptions, CancellationToken, IAsyncEnumerable<StreamingResponseUpdate>>`

`ChatAgent.Create()` wraps `ChatClient` similarly.

**Target Evolution:**
SDK agents call `ILlmSdkClient` methods directly — no delegate wrapping needed
since the client is already an abstraction. The agent takes `ILlmSdkClient` and
calls `CreateResponseAsync` / `CreateResponseStreamAsync` (or chat equivalents).

### 2. Streaming Event Processing

**Current Implementation:**
- AskAgent handles `StreamingResponseUpdate` subtypes from OpenAI SDK
- ChatAgent handles `StreamingChatCompletionUpdate` subtypes from OpenAI SDK

**Target Evolution:**
- SdkAskAgent handles `ResponseStreamEvent` subtypes from LlmSdk
  (OutputTextDeltaEvent, ResponseCompletedEvent, ResponseFailedEvent, etc.)
- SdkChatAgent handles `ChatCompletionChunk` from LlmSdk

### 3. Tool Support

**Current Implementation:**
Both agents support tool calling with multi-turn loops (up to 10 iterations).
Tools implement `ILocalTool` and are dispatched by `IToolRegistry`.

**Target Evolution:**
MVP ships without `--tools` for SDK commands. The SDK returns raw model types
(not OpenAI SDK types), so tool extraction and round-trip would require new
adapter logic. This is deferred — single-turn only.

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| ILlmSdkClient interface | ✅ | Full API: response, chat, streaming, models |
| LlmSdkClient implementation | ✅ | Wired via AddLlmSdk() DI |
| ResponseStreamEvent hierarchy | ✅ | 20+ typed event records |
| ChatCompletionChunk model | ✅ | Streaming chat events |
| ResponseExtensions.GetOutputText() | ✅ | Text extraction from Response |
| ChatCompletionExtensions.GetMessageText() | ✅ | Text extraction from ChatCompletionResponse |
| AskRequest DTO | ✅ | Reusable for SDK commands |
| System.CommandLine registration | ✅ | Pattern established by ask/chat |

### Needed

| Component | Status | Source |
|-----------|--------|--------|
| SdkAskAgent.cs | ❌ | Follow AskAgent pattern with ILlmSdkClient |
| SdkChatAgent.cs | ❌ | Follow ChatAgent pattern with ILlmSdkClient |
| Program.cs sdk-ask/sdk-chat commands | ❌ | Follow ask/chat registration pattern |
| llm-cli → llm-sdk project reference | ❌ | New ProjectReference |
| Test matrix SDK rows | ❌ | Extend backlog/test-matrix.md |
| Test matrix scripts SDK cases | ❌ | Extend test-matrix.sh / test-matrix.ps1 |

## Key Insights

### What Works Well

1. The delegate-based agent pattern makes testing trivial — SDK agents can follow the same approach if desired, or take ILlmSdkClient directly since it's already an interface
2. AskRequest DTO already captures all the flags SDK commands need
3. LlmSdk handles auth lifecycle internally (credential reload on 401) — no Worker needed
4. Extension methods (GetOutputText, GetMessageText) provide clean text extraction

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| SDK types differ from OpenAI SDK types | New agent classes (can't reuse AskAgent/ChatAgent) |
| No tool support in LlmSdk client surface | Defer --tools for SDK commands to future work |
| llm-cli currently has no llm-sdk dependency | Add ProjectReference |
| SDK streaming uses different event types | Handle ResponseStreamEvent / ChatCompletionChunk |
| ServiceProvider lifecycle in CLI | Build and dispose per-command invocation |
