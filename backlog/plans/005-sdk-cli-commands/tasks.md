# SDK CLI Commands Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

## Dependencies

```
Phase 1 (Project Setup) ──► Phase 2 (Agents) ──► Phase 3 (Wiring)
                                                       │
                                                       ▼
                                                 Phase 4 (Docs & Matrix)
```

## Phase 1: Project Setup

### Project Reference
- [ ] T001 [IMPL] Add `<ProjectReference>` to `llm-sdk.csproj` in `llm-cli.csproj`
- [ ] T002 [IMPL] Verify `dotnet build src/llm-cli/llm-cli.csproj` succeeds with new reference

## Phase 2: SDK Agents

### SdkAskAgent
- [ ] T003 [TEST] Write test: non-streaming returns output text from Response
- [ ] T004 [TEST] Write test: streaming writes OutputTextDeltaEvent deltas to writer
- [ ] T005 [TEST] Write test: non-streaming writes error when GetOutputText() returns null
- [ ] T006 [TEST] Write test: streaming writes error on ResponseFailedEvent
- [ ] T007 [IMPL] Implement `SdkAskAgent` with `RunNonStreamingAsync` and `RunStreamingAsync`

### SdkChatAgent
- [ ] T008 [TEST] Write test: non-streaming returns message text from ChatCompletionResponse
- [ ] T009 [TEST] Write test: streaming writes ChatCompletionChunk delta content to writer (null-safe: skip null Choices/Delta/Content)
- [ ] T010 [TEST] Write test: non-streaming writes error when GetMessageText() returns null
- [ ] T011 [IMPL] Implement `SdkChatAgent` with `RunNonStreamingAsync` and `RunStreamingAsync`

## Phase 3: CLI Wiring

### Program.cs Integration
- [ ] T012 [IMPL] Register `sdk-ask` subcommand in Program.cs (same options as `ask` minus `--tools` and `--endpoint`; default model gpt-5.4-mini)
- [ ] T013 [IMPL] Register `sdk-chat` subcommand in Program.cs (same options as `chat` minus `--tools` and `--endpoint`; default model gpt-5-mini)
- [ ] T014 [IMPL] Wire command handlers: `AddLogging()` + `AddLlmSdk()`, build ServiceProvider, resolve `ILlmSdkClient`, call agent
- [ ] T015 [IMPL] Update `help.txt` with sdk-ask and sdk-chat usage

## Phase 4: Documentation & Test Matrix

### Documentation
- [ ] T016 [IMPL] Update CONTRIBUTING.md to note llm-cli's dual role (HTTP proxy + SDK reference)
- [ ] T017 [IMPL] Add SDK surface rows to `backlog/test-matrix.md`

### Test Matrix Scripts
- [ ] T018 [IMPL] Add SDK test cases to `scripts/test-matrix.sh`
- [ ] T019 [IMPL] Add SDK test cases to `scripts/test-matrix.ps1`

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Project Setup | T001-T002 | 0 | 2 |
| Phase 2: SDK Agents | T003-T011 | 7 | 2 |
| Phase 3: CLI Wiring | T012-T015 | 0 | 4 |
| Phase 4: Docs & Matrix | T016-T019 | 0 | 4 |
| **Total** | **19** | **7** | **12** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/llm-cli/llm-cli.csproj` compiles
- [ ] `dotnet test tests/llm-cli.Tests/ --no-restore` passes
- [ ] `llm sdk-ask "Hello"` returns a response (live, requires credentials)
- [ ] `llm sdk-chat "Hello"` returns a response (live, requires credentials)
