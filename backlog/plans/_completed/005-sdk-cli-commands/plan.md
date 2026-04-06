# Plan: SDK CLI Commands (sdk-ask, sdk-chat)

## Summary

Add `llm sdk-ask` and `llm sdk-chat` subcommands to llm-cli that call
ILlmSdkClient in-process, bypassing the proxy. This proves the SDK surface
works end-to-end and gives users a no-proxy option.

## Architecture

```
llm-cli
├── ask ────────► OpenAI SDK ──► HTTP ──► llm-svc ──► Copilot API
├── chat ───────► OpenAI SDK ──► HTTP ──► llm-svc ──► Copilot API
│
├── sdk-ask ────► ILlmSdkClient ──────────────────► Copilot API
└── sdk-chat ──► ILlmSdkClient ──────────────────► Copilot API
```

## Detailed Architecture

```
Program.cs
    │
    ├── "sdk-ask" command handler
    │       │
    │       ├── Build ServiceCollection
    │       ├── AddLlmSdk(opt => opt.DefaultModel = model)
    │       ├── BuildServiceProvider()
    │       ├── GetRequiredService<ILlmSdkClient>()
    │       └── SdkAskAgent.RunAsync(client, request, writer)
    │
    └── "sdk-chat" command handler
            │
            └── (same pattern with SdkChatAgent)
```

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| Program.cs | Register sdk-ask/sdk-chat commands, build DI | SdkAskAgent, SdkChatAgent |
| SdkAskAgent | Send prompt via Responses API, stream/print output | ILlmSdkClient |
| SdkChatAgent | Send prompt via Chat Completions API, stream/print output | ILlmSdkClient |
| AskRequest | Shared request DTO (reused from existing) | All agents |

### Data Flow: sdk-ask Streaming

```
User ──► Program.cs ──► SdkAskAgent.RunStreamingAsync()
                              │
                              ├── Build CreateResponseRequest
                              ├── client.CreateResponseStreamAsync(request)
                              │
                              │   ┌──────────────────────────────┐
                              │   │ ResponseStreamEvent loop:     │
                              │   │  OutputTextDeltaEvent → write │
                              │   │  ResponseCompletedEvent → done│
                              │   │  ResponseFailedEvent → error  │
                              │   └──────────────────────────────┘
                              │
                              └── writer.WriteLine() at end
```

## File Structure

```
src/llm-cli/
├── Program.cs              # MODIFY: add sdk-ask, sdk-chat subcommands
├── SdkAskAgent.cs          # NEW: Responses API agent via ILlmSdkClient
├── SdkChatAgent.cs         # NEW: Chat Completions API agent via ILlmSdkClient
├── AskRequest.cs           # UNCHANGED: reused
├── llm-cli.csproj          # MODIFY: add llm-sdk ProjectReference
├── help.txt                # MODIFY: add sdk-ask, sdk-chat usage
└── ...
tests/llm-cli.Tests/
├── SdkAskAgentTests.cs     # NEW: unit tests for SdkAskAgent
├── SdkChatAgentTests.cs    # NEW: unit tests for SdkChatAgent
└── ...
backlog/
├── test-matrix.md          # MODIFY: add SDK surface rows
scripts/
├── test-matrix.sh          # MODIFY: add SDK test cases
└── test-matrix.ps1         # MODIFY: add SDK test cases
```

## Critical: DI Lifecycle Management

**Problem**: SDK commands build their own ServiceProvider (AddLlmSdk() wires
HttpClient, credential stores, CopilotClient). This must be properly disposed.

**Solution**: Build and dispose per-invocation in the command handler:
```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddLlmSdk(opt => opt.DefaultModel = model);
using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ILlmSdkClient>();
```

Note: `AddLogging()` is required — `CopilotClient` and credential stores
depend on `ILogger<T>`. Without it, resolution of `ILlmSdkClient` fails.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Agent takes ILlmSdkClient directly | Interface, not delegates | SDK client is already an abstraction; delegates add no value |
| Static RunAsync methods | No constructor state | Simpler than AskAgent pattern; no multi-turn state needed |
| DI built in command handler | Not in agent | Agent stays testable; handler owns lifecycle |
| AddLogging() in DI | Required | CopilotClient and credential stores need ILogger&lt;T&gt; |
| No --tools flag | Deferred | SDK types differ from OpenAI SDK; tool adapter is separate work |
| No --endpoint flag | Not applicable | SDK goes direct to upstream; no proxy |
| sdk-ask defaults to gpt-5.4-mini | Match ask | Same default model as the ask command |
| sdk-chat defaults to gpt-5-mini | Match chat | Same default model as the chat command |
| Null-safe streaming chunk handling | Defensive | ChatCompletionChunk.Choices can be null/empty; Delta.Content can be null |

## Implementation Phases

Phase 1: Core agents and commands (SdkAskAgent, SdkChatAgent, Program.cs wiring)
Phase 2: Tests (unit tests for both agents)
Phase 3: Documentation and test matrix updates

Details in tasks.md.

## Files to Modify

| File | Change |
|------|--------|
| src/llm-cli/llm-cli.csproj | Add ProjectReference to llm-sdk |
| src/llm-cli/Program.cs | Register sdk-ask and sdk-chat subcommands |
| src/llm-cli/help.txt | Add sdk-ask and sdk-chat usage |
| backlog/test-matrix.md | Add SDK surface coverage rows |
| scripts/test-matrix.sh | Add SDK test cases |
| scripts/test-matrix.ps1 | Add SDK test cases |
| CONTRIBUTING.md | Note CLI's dual role (proxy + SDK reference) |

## New Files

| File | Purpose |
|------|---------|
| src/llm-cli/SdkAskAgent.cs | Responses API agent via ILlmSdkClient |
| src/llm-cli/SdkChatAgent.cs | Chat Completions API agent via ILlmSdkClient |
| tests/llm-cli.Tests/SdkAskAgentTests.cs | Unit tests for SdkAskAgent |
| tests/llm-cli.Tests/SdkChatAgentTests.cs | Unit tests for SdkChatAgent |

## Verification

1. `dotnet build src/llm-cli/llm-cli.csproj` — compiles
2. `dotnet test tests/llm-cli.Tests/ --no-restore` — unit tests pass
3. `llm sdk-ask "Hello"` — returns response (live, requires credentials)
4. `llm sdk-chat "Hello"` — returns response (live, requires credentials)
5. `./scripts/test-matrix.sh` — SDK rows pass (live, requires credentials)

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| SDK auth fails in CLI context | CopilotClient handles reload; same credential stores |
| ServiceProvider leak | await using ensures disposal |
| Test flakiness | SDK path hits same upstream; same models |

## Limitations (MVP)

1. No `--tools` support — requires tool adapter for SDK response types
2. No multi-turn conversation — single prompt, single response
3. No `sdk-models` command — ListModelsAsync not exercised
