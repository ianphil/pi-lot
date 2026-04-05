---
title: "SDK CLI Commands (sdk-ask, sdk-chat)"
status: open
priority: medium
created: 2026-04-05
---

# SDK CLI Commands (sdk-ask, sdk-chat)

## Summary

Add `llm sdk-ask` and `llm sdk-chat` subcommands to `llm-cli` that exercise `CopilotLlmClient` in-process, proving the SDK surface works end-to-end from a real consumer. Extend the test matrix to cover the SDK path.

## Motivation

The existing `ask` and `chat` commands prove the HTTP proxy API works via the OpenAI .NET SDK. But the new `CopilotLlmClient` SDK (feature 003) has no real consumer exercising it beyond unit tests. Adding `sdk-ask`/`sdk-chat` turns `llm-cli` into a reference implementation for *both* surfaces — HTTP proxy and embedded library — and lets the test matrix script compare them side-by-side.

## Proposal

### Goals

- `llm sdk-ask` calls `ICopilotLlmClient.CreateResponseAsync` / `CreateResponseStreamAsync` in-process
- `llm sdk-chat` calls `ICopilotLlmClient.CreateChatCompletionAsync` / `CreateChatCompletionStreamAsync` in-process
- Same flags as existing commands: `-m`, `--no-stream`, `--tools`
- No proxy needed — SDK commands go directly to upstream via `CopilotClient`
- Extend `test-matrix.sh` / `test-matrix.ps1` with SDK surface rows

### Non-Goals

- Replacing `ask`/`chat` — the HTTP commands stay as the proxy reference implementation
- Multi-turn conversation support for SDK commands (single-turn only for now)
- SDK-specific flags beyond what `ask`/`chat` already support

## Design

- `llm-cli.csproj` adds a `<ProjectReference>` to `CopilotLlm/CopilotLlm.csproj`
- New `SdkAskAgent.cs`: builds a `ServiceCollection`, calls `AddCopilotLlm()`, resolves `ICopilotLlmClient`, runs `CreateResponseAsync` or `CreateResponseStreamAsync`, prints output text (or streams deltas)
- New `SdkChatAgent.cs`: same pattern but for chat completions
- `Program.cs` registers `sdk-ask` and `sdk-chat` as System.CommandLine subcommands with the same option shapes as `ask`/`chat`
- Auth lifecycle is handled by `CopilotClient` inside DI — no proxy dependency
- Update CONTRIBUTING.md architecture note to reflect that llm-cli now also serves as SDK reference consumer
- Extend `backlog/test-matrix.md` with an SDK surface section

## Tasks

- [ ] Add `CopilotLlm` project reference to `llm-cli.csproj`
- [ ] Create `SdkAskAgent.cs` — wire DI, call `ICopilotLlmClient` responses API, print output
- [ ] Create `SdkChatAgent.cs` — wire DI, call `ICopilotLlmClient` chat API, print output
- [ ] Register `sdk-ask` and `sdk-chat` subcommands in `Program.cs`
- [ ] Add SDK surface rows to `backlog/test-matrix.md`
- [ ] Extend `scripts/test-matrix.sh` and `scripts/test-matrix.ps1` with SDK test cases
- [ ] Update CONTRIBUTING.md to note llm-cli's dual role (HTTP + SDK reference)

## Open Questions

- Should `sdk-ask` support `--tools` with local tool execution (like `AskAgent` does), or just forward tool definitions and print the raw function_call response? Starting with raw output is simpler; tool execution can be added later.
