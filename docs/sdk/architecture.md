# LlmSdk Architecture

`LlmSdk` follows ports and adapters. Core models and translators own portable
semantics. Proxy interfaces define host-facing ports. Infrastructure implements
Copilot auth, HTTP, credential stores, and model catalogue access.

```text
llm-sdk/Infrastructure -> llm-sdk/Core <- llm-sdk/Client
                                 ^
                     ServiceCollectionExtensions
                                 ^
                         src/llm-svc/Program.cs
```

## Public layers

| Layer | Namespace | Owns |
|---|---|---|
| Registration | `LlmSdk` | `AddLlmSdk()` DI wiring |
| Client | `LlmSdk.Client` | `ILlmSdkClient`, options, stream events, exceptions, helpers |
| Core models | `LlmSdk.Core.Models` | Portable context DTOs plus raw Responses/Chat DTOs |
| Proxy ports | `LlmSdk.Proxy` | Host-facing service, auth, model, and credential contracts |
| Infrastructure | `LlmSdk.Infrastructure` | Copilot HTTP and platform credential adapters |

## Consumer boundaries

`llm-svc`, `llm-cli`, `llm-agent`, and `llm-ui` are consumers of the SDK, not
owners of SDK behavior. Shared semantics should move into SDK Core or Client
only when multiple public SDK surfaces consume them.

`src/llm-sdk/Core` and `src/llm-sdk/Proxy` must not depend on Infrastructure,
HTTP clients, host frameworks, or deployable projects.
