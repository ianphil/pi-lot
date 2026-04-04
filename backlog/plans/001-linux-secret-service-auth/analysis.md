# Linux Secret Service Auth Analysis

## Executive Summary

| Pattern | Integration Point |
|---------|-------------------|
| Adapter extraction | Move platform credential lookup out of `Infrastructure/CopilotClient.cs` and behind an Infrastructure-only credential-store abstraction |
| Runtime composition | Register Windows, Linux, or no-op credential stores in `Program.cs` using runtime OS checks |
| Metadata-assisted selection | Read non-secret Copilot CLI login metadata from `~/.copilot/config.json` on Linux to prefer `last_logged_in_user` without parsing secrets from config |
| Degraded startup | Preserve the current startup and `/health` behavior when no credential source is available |

## Architecture Comparison

### Current Architecture

```text
COPILOT_TOKEN env var
        |
        v
Program.cs ------------------------------+
        |                                |
        v                                |
  CopilotClient.TryLoadCredential()      |
        |                                |
        +--> env var                     |
        +--> Windows Credential Manager  |
        +--> unsupported OS => error ----+

Worker.ValidateTokenAsync() -> CopilotClient reloads from same inline logic
```

### Target Architecture

```text
Program.cs
  |
  +--> ICopilotCredentialStore registration
  |      +--> WindowsCredentialStore
  |      +--> LinuxSecretServiceCredentialStore
  |      \--> NoOpCopilotCredentialStore
  |
  \--> CopilotClient
         |
         +--> COPILOT_TOKEN env var (highest priority)
         +--> injected credential store
         \--> degraded startup / reload logging

LinuxSecretServiceCredentialStore
  |
  +--> CopilotCliConfigMetadataReader (non-secret login metadata)
  \--> Secret Service over D-Bus (`service=copilot-cli`)
```

## Pattern Mapping

### 1. Auth Loading in the HTTP Adapter

**Current Implementation:**

- `Infrastructure/CopilotClient.cs` owns token state, startup loading, 401 reload, and model-cache reuse.
- `TryLoadCredential()` checks `COPILOT_TOKEN` first, then Windows Credential Manager, then falls back to degraded mode.

**Target Evolution:**

- Keep token ownership and reload behavior in `CopilotClient`.
- Replace inline platform branches with an injected credential store so the HTTP adapter stops knowing Windows-only lookup details.

### 2. Platform-Specific Infrastructure

**Current Implementation:**

- `Infrastructure/CredentialManager.cs` is a static Windows P/Invoke helper.
- `Program.cs` already uses runtime OS checks for Windows service and Event Log registration.

**Target Evolution:**

- Keep platform-specific secret access in `Infrastructure/`.
- Add one runtime registration point in `Program.cs` so Windows behavior stays unchanged and Linux behavior is added without new Core dependencies.

### 3. Test Isolation Through Fakes and Stubbed HTTP

**Current Implementation:**

- `llm-svc.Tests/Unit/CopilotClientTests.cs` uses a stub `HttpClient` and environment-variable setup.
- `llm-svc.Tests/Integration/ResponsesWebApplicationFactory.cs` swaps `IAuthProvider` and `IModelProvider` with `FakeModelProvider`.

**Target Evolution:**

- Extend the existing unit-test style to inject fake credential stores into `CopilotClient`.
- Add focused Linux store tests around metadata preference, GitHub.com filtering, and D-Bus failure handling instead of depending on a real desktop keyring.

### 4. User-Facing Auth Documentation

**Current Implementation:**

- `README.md` documents Windows Credential Manager as the only automatic auth path.

**Target Evolution:**

- Document three modes clearly: env-var override on all platforms, Windows secure-store lookup, and Linux desktop Secret Service lookup with explicit env-var fallback for headless/container usage.

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| `IAuthProvider` startup and reload flow | ✅ | Startup load, 401 reload, and background validation already exist |
| Windows credential lookup | ✅ | `CredentialManager.GetCredential()` supports exact and prefix matches |
| Degraded startup and `/health` reporting | ✅ | Service starts unauthenticated and reports degraded health |
| Integration-test fake auth provider | ✅ | `FakeModelProvider` already models authenticated vs unauthenticated states |

### Needed

| Component | Status | Source |
|-----------|--------|--------|
| Infrastructure credential-store abstraction | ❌ | New Infrastructure interface for platform lookup |
| Linux Secret Service credential store | ❌ | New D-Bus-backed adapter using Secret Service item lookup |
| Copilot CLI login metadata reader | ❌ | New helper for `last_logged_in_user` and `logged_in_users` |
| Platform-specific DI registration | ❌ | Extend `Program.cs` runtime registration |
| Linux auth unit tests | ❌ | New unit tests around D-Bus failure and account preference |
| README Linux auth guidance | ❌ | Extend existing auth documentation |

## Key Insights

### What Works Well

1. The current service already separates business logic from external details; this change fits cleanly if the new abstraction stays in `Infrastructure`.
2. `COPILOT_TOKEN` already provides a safe cross-platform escape hatch, so Linux headless/container scenarios do not need a new auth mechanism.
3. Startup degradation, `/health`, and 401 reload behavior are already the right operational shape and should be preserved rather than redesigned.

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| `CopilotClient` contains Windows-only credential logic | Inject a credential-store abstraction and keep `CopilotClient` focused on token lifecycle |
| Linux desktop sessions cannot reuse Copilot CLI sign-in | Add a Secret Service adapter keyed to the same `copilot-cli` attributes the CLI uses |
| Multiple Linux accounts need deterministic selection | Prefer `last_logged_in_user` metadata when present, otherwise fall back to the first GitHub.com credential in a deterministic order |
| Current unit tests assume env-var auth only | Add store-aware tests and Linux-store-specific tests |
| README implies Windows-only automatic auth | Document Linux desktop and headless/container expectations explicitly |

### Architecture Boundary Decision

The new credential-store abstraction should remain in `Infrastructure`, not `Core/Ports`. Secret retrieval is an adapter concern for `CopilotClient`; Core services do not need to reason about keychains, D-Bus, or OS-specific credential stores.
