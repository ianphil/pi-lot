# Credential Store Contract

## Purpose

Define how `CopilotClient` asks platform-specific Infrastructure code for a Copilot token without embedding Windows or Linux lookup logic directly in the HTTP adapter.

## Interface Shape

```csharp
namespace LlmSvc.Infrastructure;

public interface ICopilotCredentialStore
{
    string DisplayName { get; }
    string? GetCredential();
}
```

## Behavioral Contract

| Rule | Description |
|------|-------------|
| Precedence | `CopilotClient` must check `COPILOT_TOKEN` before calling the store |
| Null semantics | `GetCredential()` returns `null` when the secure store is unavailable, empty, or not applicable |
| Non-interactive behavior | Implementations must not prompt the user or block waiting for UI-driven unlock flows |
| Logging | Implementations may log diagnostic context, but callers must never log a full token |

## Platform Implementations

### WindowsCredentialStore

- Uses `CredentialManager.GetCredential("copilot-cli/https://github.com")`
- Preserves the existing exact-match then prefix-match behavior

### LinuxSecretServiceCredentialStore

- Searches Secret Service items where `service = copilot-cli`
- Filters to accounts beginning with `https://github.com:`
- Prefers `https://github.com:<last_logged_in_user>` when metadata is available
- Falls back to the first deterministic GitHub.com match

### NoOpCopilotCredentialStore

- Always returns `null`
- Used on unsupported platforms

## Caller Obligations

- `CopilotClient` owns token caching and reload behavior.
- `Program.cs` owns choosing which implementation is registered.
- Unit tests must verify env-var precedence separately from store-specific behavior.
