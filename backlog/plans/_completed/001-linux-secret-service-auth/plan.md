# Plan: Linux Secret Service Auth

## Summary

Refactor credential lookup inside `CopilotClient` so the token lifecycle stays the same while the platform-specific secret-source logic becomes injectable. The feature adds a Linux Secret Service adapter backed by direct D-Bus calls, a metadata reader for Copilot CLI account preference, and runtime DI wiring in `Program.cs`, while preserving `COPILOT_TOKEN` precedence and the existing Windows path.

## Architecture

```text
                    +----------------------+
                    |  COPILOT_TOKEN env   |
                    +----------+-----------+
                               |
                               v
                       +-------+--------+
                       |  CopilotClient |
                       | token lifecycle|
                       +---+---------+--+
                           |         |
               startup / 401|         |models / proxy requests
                      reload|         |
                           v         v
               +-----------+------------------+
               | ICopilotCredentialStore      |
               +-----------+------------------+
                           |
         +-----------------+-------------------+
         |                 |                   |
         v                 v                   v
 WindowsCredentialStore  LinuxSecretService  NoOpCopilot
   -> CredentialManager    CredentialStore     CredentialStore
                              |
                              +--> CopilotCliConfigMetadataReader
                              \--> Secret Service item search
```

## Detailed Architecture

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| `Program.cs` | Registers the correct credential store for the current OS | `CopilotClient`, DI container |
| `CopilotClient` | Owns `_token`, startup loading, validation, and 401 reload | `ICopilotCredentialStore`, `IHttpClientFactory` |
| `ICopilotCredentialStore` | Small Infrastructure contract for secure-store lookup | Windows, Linux, and no-op implementations |
| `WindowsCredentialStore` | Wraps existing Credential Manager lookup without behavior change | `CredentialManager` |
| `LinuxSecretServiceCredentialStore` | Resolves the preferred Copilot item from Secret Service and reads its secret | `Tmds.DBus.Protocol`, `CopilotCliConfigMetadataReader` |
| `CopilotCliConfigMetadataReader` | Reads non-secret login metadata (`last_logged_in_user`, `logged_in_users`) | `~/.copilot/config.json` |
| `NoOpCopilotCredentialStore` | Returns no credential on unsupported platforms | `CopilotClient` |

### Data Flow: Linux Startup Credential Load

```text
Program.cs
  -> inject LinuxSecretServiceCredentialStore into CopilotClient
CopilotClient.TryLoadCredential()
  -> check COPILOT_TOKEN
  -> ask LinuxSecretServiceCredentialStore.GetCredential()
       -> read preferred login from CopilotCliConfigMetadataReader
       -> search Secret Service items where service=copilot-cli
       -> filter GitHub.com account entries
       -> prefer https://github.com:<last_logged_in_user> when available
       -> otherwise pick first deterministic GitHub.com match
       -> return secret bytes as token
  -> store token and log CredentialLoaded
```

### Data Flow: 401 Reload

```text
Upstream 401
  -> CopilotClient.HandleUnauthorized()/ValidateTokenAsync catch
  -> log TokenExpired
  -> rerun the same credential resolution order
  -> update _token if a newer/valid credential is found
  -> leave service degraded if no credential is available
```

## File Structure

```text
Program.cs                                              # MODIFY: register OS-specific credential stores
llm-svc.csproj                                          # MODIFY: add D-Bus dependency
README.md                                               # MODIFY: document Linux desktop and headless auth modes
Infrastructure/
├── CopilotClient.cs                                    # MODIFY: use injected credential store
├── CredentialManager.cs                                # KEEP: native Windows helper used by wrapper
├── ICopilotCredentialStore.cs                          # NEW: infrastructure contract for secure-store lookup
├── WindowsCredentialStore.cs                           # NEW: wrapper around CredentialManager
├── LinuxSecretServiceCredentialStore.cs                # NEW: Secret Service lookup over D-Bus
├── CopilotCliConfigMetadataReader.cs                   # NEW: non-secret config metadata reader
└── NoOpCopilotCredentialStore.cs                       # NEW: unsupported-platform fallback
llm-svc.Tests/
└── Unit/
    ├── CopilotClientTests.cs                           # MODIFY: env/store precedence and reload tests
    ├── LinuxSecretServiceCredentialStoreTests.cs       # NEW: D-Bus and account-selection tests
    └── CopilotCliConfigMetadataReaderTests.cs          # NEW: metadata parsing tests
```

## Critical: Preferred Account Without Config-Based Secret Parsing

**Problem**: The service must prefer the same Linux account the Copilot CLI last used, but the quick plan explicitly rejects `~/.copilot/config.json` as the primary secret source.

**Solution**: Split the concern in two. Secret retrieval stays in Secret Service, while `CopilotCliConfigMetadataReader` reads only non-secret metadata such as `last_logged_in_user` and `logged_in_users` to choose the right Secret Service account key. No token bytes are ever read from config.

## Implementation Phases

1. Introduce the Infrastructure credential-store contract and move Windows lookup behind it.
2. Add Linux Secret Service lookup plus CLI metadata preference.
3. Wire DI, extend tests, and update README guidance.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Credential abstraction location | Keep `ICopilotCredentialStore` in `Infrastructure` | Secret-store access is adapter logic, not Core business behavior |
| Linux dependency | Direct `Tmds.DBus.Protocol` wrapper | Matches the issue research recommendation for the more conservative long-term dependency |
| Account selection | Prefer `last_logged_in_user`, otherwise first deterministic GitHub.com match | Aligns with Copilot CLI behavior while avoiding ambiguous random selection |
| Windows refactor style | Wrap existing `CredentialManager` helper | Preserves working native code and reduces regression risk |
| Locked/unavailable keyring behavior | Return null, do not prompt | Keeps service startup non-interactive and safe for background execution |

## Configuration Example

```bash
# Explicit override remains valid everywhere
COPILOT_TOKEN=ghu_xxx dotnet run
```

```json
// ~/.copilot/config.json (metadata only; not a secret source)
{
  "last_logged_in_user": "ianphil",
  "logged_in_users": {
    "ianphil": {}
  }
}
```

## Files to Modify

| File | Change |
|------|--------|
| `Program.cs` | Register Windows/Linux/no-op credential stores before `CopilotClient` construction |
| `Infrastructure/CopilotClient.cs` | Replace inline OS branches with injected store usage |
| `README.md` | Document platform-specific auth behavior and env-var fallback |
| `llm-svc.csproj` | Add the direct D-Bus dependency used by the Linux store |
| `llm-svc.Tests/Unit/CopilotClientTests.cs` | Add store-aware precedence and reload tests |

## New Files

| File | Purpose |
|------|---------|
| `Infrastructure/ICopilotCredentialStore.cs` | Shared Infrastructure contract for secure-store lookup |
| `Infrastructure/WindowsCredentialStore.cs` | Windows wrapper around `CredentialManager` |
| `Infrastructure/LinuxSecretServiceCredentialStore.cs` | Linux Secret Service implementation |
| `Infrastructure/CopilotCliConfigMetadataReader.cs` | Reads non-secret Copilot CLI login metadata |
| `Infrastructure/NoOpCopilotCredentialStore.cs` | Unsupported-platform fallback |
| `llm-svc.Tests/Unit/LinuxSecretServiceCredentialStoreTests.cs` | Linux secure-store test coverage |
| `llm-svc.Tests/Unit/CopilotCliConfigMetadataReaderTests.cs` | Metadata parsing test coverage |

## Verification

1. `dotnet test llm-svc.Tests --filter "Category!=Smoke" --no-restore`
2. Manual verification on a Linux desktop session with a signed-in Copilot CLI account and no `COPILOT_TOKEN`
3. On Windows environments where the scheduled task is active, stop `CopilotLlmProxy` before running service tests.

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| D-Bus connection failures in headless/container Linux | Catch connection errors in the Linux store and return null |
| Wrong account chosen on Linux | Read CLI metadata first, then use deterministic fallback ordering |
| Regression in Windows auth | Preserve `CredentialManager` implementation and cover Windows wrapper behavior with unit tests |
| Over-coupling to Copilot CLI internals | Keep service/account identifiers centralized and documented in contracts |

## Limitations (MVP)

1. Linux keyrings that require user prompting to unlock are treated as unavailable.
2. The feature targets GitHub.com account keys and does not add GitHub Enterprise host selection yet.
3. macOS secure-store support is intentionally deferred.

## References

- `Infrastructure/CopilotClient.cs`
- `Infrastructure/CredentialManager.cs`
- `Program.cs`
- [Secret Service API 0.2 DRAFT](https://specifications.freedesktop.org/secret-service-spec/latest-single/)
- [Issue #1: Linux Copilot credential lookup](https://github.com/ianphil/pi-lot/issues/1)
