# Specification: SDK CLI Commands (sdk-ask, sdk-chat)

## Overview

### Problem Statement

The LlmSdk library has a full client surface (ILlmSdkClient) but no real
consumer beyond unit tests and the llm-svc host (which uses lower-level
services, not the client). Without a reference consumer, SDK regressions
go undetected until downstream adopters hit them.

### Solution Summary

Add `llm sdk-ask` and `llm sdk-chat` subcommands that exercise the SDK
client in-process — bypassing the proxy entirely — proving the library
works end-to-end from credential resolution through response delivery.

### Business Value

| Benefit | Impact |
|---------|--------|
| SDK regression detection | Catch client-surface bugs before NuGet consumers do |
| Reference implementation | SDK adopters can copy patterns from CLI code |
| Test matrix coverage | Side-by-side comparison of proxy vs SDK paths |
| No-proxy usage | Users can call Copilot directly without running llm-svc |

## User Stories

### CLI User

**As a** CLI user, I want to send prompts directly through the SDK without
starting the proxy, so that I can use Copilot with a simpler setup.

**Acceptance Criteria:**
- `llm sdk-ask "prompt"` returns a response without requiring llm-svc
- `llm sdk-chat "prompt"` returns a response without requiring llm-svc
- Output streams by default, `--no-stream` disables streaming
- `-m` selects the model (defaults to gpt-5.4-mini)
- `-s` sets system instructions

### SDK Adopter

**As a** .NET developer evaluating LlmSdk, I want to see a working reference
consumer, so that I can copy the DI wiring and usage patterns.

**Acceptance Criteria:**
- SdkAskAgent demonstrates AddLlmSdk() → ILlmSdkClient → CreateResponseAsync
- SdkChatAgent demonstrates AddLlmSdk() → ILlmSdkClient → CreateChatCompletionAsync
- Streaming usage demonstrates IAsyncEnumerable consumption

### Maintainer

**As a** project maintainer, I want the test matrix to cover SDK paths,
so that I can verify both proxy and SDK surfaces in one pass.

**Acceptance Criteria:**
- test-matrix.sh includes SDK surface test cases
- test-matrix.ps1 includes SDK surface test cases
- backlog/test-matrix.md documents SDK coverage

## Functional Requirements

### FR-1: sdk-ask Subcommand

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | Accepts a prompt argument (required) |
| FR-1.2 | `-m` / `--model` option (default: gpt-5.4-mini, matching `ask`) |
| FR-1.3 | `-s` / `--system` option for system instructions |
| FR-1.4 | `--no-stream` flag disables streaming (default: streaming) |
| FR-1.5 | Calls ILlmSdkClient.CreateResponseStreamAsync (or CreateResponseAsync) |
| FR-1.6 | Streams text deltas to stdout, prints newline at end |
| FR-1.7 | Prints error message and exits non-zero on failure |

### FR-2: sdk-chat Subcommand

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | Accepts a prompt argument (required) |
| FR-2.2 | `-m` / `--model` option (default: gpt-5-mini, matching `chat`) |
| FR-2.3 | `-s` / `--system` option for system instructions |
| FR-2.4 | `--no-stream` flag disables streaming (default: streaming) |
| FR-2.5 | Calls ILlmSdkClient.CreateChatCompletionStreamAsync (or Async) |
| FR-2.6 | Streams text deltas to stdout, prints newline at end |
| FR-2.7 | Prints error message and exits non-zero on failure |

### FR-3: Test Matrix

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | test-matrix.sh adds SDK surface rows for sdk-ask and sdk-chat |
| FR-3.2 | test-matrix.ps1 adds matching SDK surface rows |
| FR-3.3 | backlog/test-matrix.md documents SDK coverage |

## Non-Functional Requirements

### Performance

| Requirement | Target |
|-------------|--------|
| Startup latency | SDK commands should start within 2s (no proxy wait) |
| Streaming latency | First token should appear as fast as proxy path |

### Security

| Requirement | Target |
|-------------|--------|
| Credential handling | Same as proxy: COPILOT_TOKEN → Windows CM → Linux SS |
| No credential exposure | No tokens logged or printed |

## Scope

### In Scope

- `sdk-ask` and `sdk-chat` subcommands
- Streaming and non-streaming modes
- Model selection and system instructions
- Test matrix updates (docs + scripts)
- CONTRIBUTING.md update for dual-role CLI

### Out of Scope

- `--tools` flag for SDK commands (deferred — requires tool adapter for SDK types)
- Multi-turn conversation support
- SDK-specific flags not present on ask/chat
- Changes to the LlmSdk library itself

### Future Considerations

- Tool calling via SDK (requires mapping ILlmSdkClient response types to tool extraction)
- `sdk-models` command using ILlmSdkClient.ListModelsAsync
- Interactive/REPL mode for SDK commands

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Commands work | sdk-ask and sdk-chat produce output | Manual test with live credentials |
| Streaming works | Text appears incrementally | Visual confirmation |
| Test matrix passes | SDK rows pass in test-matrix.sh | Script exit code |
| No proxy required | SDK commands work without llm-svc running | Test with proxy stopped |

## Assumptions

1. Users have Copilot CLI credentials available (same as proxy)
2. The LlmSdk library's AddLlmSdk() handles all DI wiring correctly
3. SDK commands are single-turn only (no conversation state)

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SDK auth fails without proxy's Worker | Low | High | CopilotClient handles credential reload internally |
| Large dependency footprint from llm-sdk | Low | Medium | llm-sdk is already a thin library |
| Test matrix flakiness with SDK path | Medium | Low | SDK path hits same upstream; same retry logic |

## Glossary

| Term | Definition |
|------|------------|
| SDK path | In-process call through ILlmSdkClient directly to Copilot API |
| Proxy path | HTTP call through llm-svc on localhost:5100 |
| Test matrix | Script that exercises all surface × model combinations |
