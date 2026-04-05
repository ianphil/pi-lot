# SDK CLI Commands — Research

**Date**: 2026-04-05
**Scope**: Internal feature — no external spec conformance required

## Summary

This feature is purely internal: it adds CLI subcommands that exercise the
existing LlmSdk client surface. No external API specification, protocol, or
standard needs conformance validation.

## Internal Conformance

### 1. LlmSdk Client API Coverage

| Method | sdk-ask | sdk-chat |
|--------|---------|----------|
| CreateResponseAsync | ✅ non-streaming | — |
| CreateResponseStreamAsync | ✅ streaming | — |
| CreateChatCompletionAsync | — | ✅ non-streaming |
| CreateChatCompletionStreamAsync | — | ✅ streaming |
| ListModelsAsync | — | — |

**Recommendation**: ListModelsAsync is not exercised by either command.
A future `sdk-models` command could cover it. Not blocking for this feature.

### 2. Existing CLI Flag Parity

| Flag | ask/chat | sdk-ask/sdk-chat | Status |
|------|----------|------------------|--------|
| prompt (argument) | ✅ | ✅ | CONFORMANT |
| -m / --model | ✅ | ✅ | CONFORMANT |
| -s / --system | ✅ | ✅ | CONFORMANT |
| --no-stream | ✅ | ✅ | CONFORMANT |
| --tools | ✅ | ❌ (deferred) | INTENTIONAL GAP |
| --endpoint | ✅ | N/A | NOT APPLICABLE |

**Recommendation**: The `--endpoint` flag is proxy-specific and does not apply
to SDK commands. The `--tools` gap is documented and deferred intentionally.

### 3. DI Lifecycle in CLI Context

The existing AskAgent/ChatAgent receive pre-built clients from Program.cs.
SDK agents need to build their own ServiceProvider via AddLlmSdk(). Key
concerns:

- **ServiceProvider disposal**: Must be disposed after command execution to
  release HttpClient and credential store resources.
- **No Worker needed**: CopilotClient handles credential lifecycle internally
  (reload on 401). The proxy's Worker is a host concern, not a library concern.

**Recommendation**: Use `await using var provider = services.BuildServiceProvider()`
in the command handler to ensure proper cleanup.

## Conclusion

No external conformance issues. The design aligns with existing CLI patterns
and the LlmSdk public API surface. The only intentional gap (--tools) is
documented and scoped for future work.
