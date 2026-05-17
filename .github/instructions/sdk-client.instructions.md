---
description: 'Design and test the public LlmSdk client surface deliberately.'
applyTo: 'src/llm-sdk/Client/**/*.cs,tests/llm-sdk.Tests/**/*.cs,tests/llm-sdk.Int/**/*.cs'
---

# SDK Client Surface

Do not treat "SDK" as one surface. The SDK contains both a client-facing API and
shared service/core classes:

- `Client/` is the ergonomic in-process SDK client surface. This is what
  `llm-agent` consumes through `ILlmSdkClient`, `Context`, `CompletionOptions`,
  `AssistantMessage`, and `AssistantStreamEvent`.
- `Core/` and `Proxy/` are the shared engine and contracts underneath. They are
  used by the SDK client and by `llm-svc` through DI services such as
  `IResponsesService`, routing, translation, validation, and DTOs.
- `Infrastructure/` implements external adapters both paths need, including
  Copilot HTTP calls, auth, credentials, and model discovery.

Before implementing an SDK issue, identify the public surface being changed:

| Surface | Shape | Typical consumers |
|---|---|---|
| SDK client | `ILlmSdkClient`, `CreateResponse*`, `Context`, `AssistantMessage`, `AssistantStreamEvent` | SDK package consumers, agent, UI |
| Service/proxy | HTTP `/responses` and `/chat/completions`, `IResponsesService`, Core routing/translation | Local proxy consumers |
| Agent | Agent loop, tools, context budget, event handling | Agent package consumers |

The surfaces may have different shapes because they are different boundaries.
The service is an HTTP proxy and should preserve wire-compatible DTO behavior.
The SDK client can expose ergonomic in-process abstractions such as `Context`.

Shared semantics belong below the boundary, usually in Core services,
translators, validators, or request normalization. If a feature is client-only,
service-only, or agent-only, make that ownership explicit and test the owning
surface plus the units underneath it.

Do not modify or add tests under `llm-agent`, `llm-svc`, `llm-cli`, or `llm-ui`
for an SDK issue unless the issue explicitly changes that package's owned
behavior. Prove SDK-owned behavior in `llm-sdk.Tests` and `llm-sdk.Int`.
