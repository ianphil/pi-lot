# 006 — llm-agent-core: Tasks

## TDD Approach

All implementation follows Red-Green-Refactor:
1. **RED:** Write a failing test that describes the expected behavior
2. **GREEN:** Write the minimum code to make it pass
3. **REFACTOR:** Clean up without changing behavior

Tests use `FakeLlmSdkClient` with delegate-based faking (no mock frameworks), following the established pattern from `tests/llm-cli.Tests/Fakes/`.

## Dependencies

```
Phase 1: Project Setup
    │
    ▼
Phase 2: Types & Contracts
    │
    ▼
Phase 3: Agent Loop
    │
    ▼
Phase 4: Integration & Validation
```

## Phase 1: Project Setup

### Project Scaffolding

- [ ] T001 [IMPL] Create `src/llm-agent/llm-agent.csproj` with `<ProjectReference>` to `src/llm-sdk/llm-sdk.csproj`, `RootNamespace=LlmAgent`, `Version=0.1.0`
- [ ] T002 [IMPL] Create `tests/llm-agent.Tests/llm-agent.Tests.csproj` with references to `llm-agent`, xunit, and test SDK packages (match `llm-sdk.Tests` pattern)
- [ ] T003 [IMPL] Add both projects to `copilot-llm.sln` under `src` and `tests` solution folders
- [ ] T004 [IMPL] Verify `dotnet build src/llm-agent/llm-agent.csproj` and `dotnet build tests/llm-agent.Tests/llm-agent.Tests.csproj` succeed

## Phase 2: Types & Contracts

### AgentContext (Typed Context Model)

- [ ] T005 [TEST] Write test: `AgentContext.SerializeInput()` with single user message produces correct JSON array
- [ ] T006 [TEST] Write test: `AgentContext.SerializeInput()` with user message + response output + tool result produces correct multi-item JSON array
- [ ] T007 [TEST] Write test: `ToolResultContextItem` serialization generates an ID and includes `call_id` and `output`
- [ ] T008 [IMPL] Define `AgentContextItem` hierarchy (`UserMessageContextItem`, `ResponseOutputContextItem`, `ToolResultContextItem`) and `AgentContext` class with `SerializeInput()`

### AgentEvent Hierarchy

- [ ] T009 [TEST] Write test: all 9 `AgentEvent` subtypes can be pattern-matched exhaustively
- [ ] T010 [IMPL] Define `AgentEvent` abstract record and all 9 derived record types in `AgentTypes.cs`

### IAgentTool & Supporting Types

- [ ] T011 [TEST] Write test: `IAgentTool` can be converted to `ResponseFunctionToolDefinition` via extension method
- [ ] T012 [IMPL] Define `IAgentTool` interface (with `JsonElement` arguments), `AgentToolResult`, `AgentToolCallResult`, and `ToToolDefinition()` extension
- [ ] T013 [IMPL] Define `AgentLoopOptions` record

### Test Infrastructure

- [ ] T014 [IMPL] Create `tests/llm-agent.Tests/Fakes/FakeLlmSdkClient.cs` (adapted from `tests/llm-cli.Tests/Fakes/`)
- [ ] T015 [IMPL] Create `tests/llm-agent.Tests/Fakes/FakeAgentTool.cs` — delegate-based `IAgentTool` for tests
- [ ] T016 [IMPL] Create `tests/llm-agent.Tests/Helpers/StreamHelpers.cs` — factory methods for building canned `ResponseStreamEvent` sequences

## Phase 3: Agent Loop

### Single Turn (No Tool Calls)

- [ ] T017 [TEST] Write test: prompt with no tool calls emits `AgentStarted → TurnStarted → MessageStarted → MessageDelta(s) → MessageEnded → TurnEnded → AgentEnded`
- [ ] T018 [TEST] Write test: `MessageDelta` events contain the `ResponseStreamEvent` from the stream
- [ ] T019 [TEST] Write test: `MessageEnded` contains the completed `Response`
- [ ] T020 [TEST] Write test: `AgentEnded` carries the typed `AgentContext`
- [ ] T021 [IMPL] Implement `AgentLoop.RunAsync` — initial version handling single turn with no tools

### Tool Execution

- [ ] T022 [TEST] Write test: response with one `ResponseFunctionCallItem` triggers tool execution; emits `ToolExecutionStarted → ToolExecutionEnded`
- [ ] T023 [TEST] Write test: tool receives parsed `JsonElement` arguments (not raw string)
- [ ] T024 [TEST] Write test: tool result is fed back in the next turn's `CreateResponseRequest.Input` as `function_call_output`
- [ ] T025 [TEST] Write test: response output items (message + function_call) are appended to typed context for next turn
- [ ] T026 [TEST] Write test: multiple tool calls in one response are executed sequentially
- [ ] T027 [IMPL] Implement tool execution in the loop — find tool, parse args, call `ExecuteAsync`, build result, append to context

### Error Handling

- [ ] T028 [TEST] Write test: tool that throws has its exception caught and returned as error result (`IsError = true`) to model
- [ ] T029 [TEST] Write test: tool name not in tools list emits `ToolExecutionStarted` + `ToolExecutionEnded` with error result
- [ ] T030 [TEST] Write test: invalid JSON in tool arguments returns error result to model
- [ ] T031 [TEST] Write test: `ResponseFailedEvent` terminates the loop and emits `AgentEnded`
- [ ] T032 [TEST] Write test: `ResponseIncompleteEvent` terminates the loop and emits `AgentEnded`
- [ ] T033 [IMPL] Implement error handling — tool exceptions, tool not found, bad args, response failure, response incomplete

### Loop Control

- [ ] T034 [TEST] Write test: loop continues until response has no tool calls (multi-turn conversation)
- [ ] T035 [TEST] Write test: `MaxTurns` stops the loop after N turns and emits `AgentEnded`
- [ ] T036 [TEST] Write test: `CancellationToken` cancellation stops the loop
- [ ] T037 [IMPL] Implement loop control — multi-turn continuation, max turns, cancellation

### Request Building

- [ ] T038 [TEST] Write test: `Instructions` from options is passed through to `CreateResponseRequest.Instructions`
- [ ] T039 [TEST] Write test: `Temperature` and `Reasoning` from options are passed through to request
- [ ] T040 [TEST] Write test: tools are converted via `ToToolDefinition()` and passed in request
- [ ] T041 [IMPL] Implement request building with full options passthrough

## Phase 4: Integration & Validation

- [ ] T042 [TEST] Write integration test: full multi-turn scenario — prompt → tool call → tool result → final response — verifying complete event sequence and typed context
- [ ] T043 [TEST] Write integration test: two consecutive tool calls across two turns (tool result triggers another tool call)
- [ ] T044 [IMPL] Final build and test validation: `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore`

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Project Setup | T001–T004 | 0 | 4 |
| Phase 2: Types & Contracts | T005–T016 | 4 | 8 |
| Phase 3: Agent Loop | T017–T041 | 19 | 6 |
| Phase 4: Integration & Validation | T042–T044 | 2 | 1 |
| **Total** | **44** | **25** | **19** |

## Final Validation

1. `dotnet build src/llm-agent/llm-agent.csproj` succeeds
2. `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore` passes all tests
3. `dotnet build copilot-llm.sln` succeeds (no regressions)
4. Agent loop can run a multi-turn tool-calling conversation with faked client
