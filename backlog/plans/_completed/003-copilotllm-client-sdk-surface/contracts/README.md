# CopilotLlmClient SDK Surface — Contracts

Interface definitions for the CopilotLlmClient SDK surface.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [copilot-llm-client.md](copilot-llm-client.md) | `CopilotLlmClient` public API surface |
| [copilot-llm-options.md](copilot-llm-options.md) | `CopilotLlmOptions` configuration contract |

## Contract Principles

- `CopilotLlmClient` is the single public entry point for SDK consumers
- All methods accept `CancellationToken` as final optional parameter
- Request-object overloads are the stable contract; convenience overloads are additive sugar
- Model names are always plain strings — no constants or enums
- Errors are communicated via exceptions, never via return-value status codes
