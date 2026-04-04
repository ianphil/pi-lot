# Specification: Linux Secret Service Auth

## Overview

### Problem Statement

`llm-svc` can currently auto-load Copilot credentials only from `COPILOT_TOKEN` or Windows Credential Manager. Linux desktop users who are already signed in to the Copilot CLI still have to export a token manually, which is worse than the existing Windows experience and unnecessary when the OS keychain already holds the credential.

### Solution Summary

Add a Linux Secret Service credential store that `CopilotClient` can use when `COPILOT_TOKEN` is absent. Preserve the current Windows behavior, prefer explicit env-var auth everywhere, and keep headless/container Linux on explicit env-var configuration when no desktop keyring session is available.

### Business Value

| Benefit | Impact |
|---------|--------|
| Linux parity with Windows desktop auth | Reduces setup friction for developers already signed in to Copilot CLI |
| Secure-store reuse | Avoids encouraging plaintext token handling for normal desktop usage |
| Stable operator behavior | Keeps container and headless setups on the existing explicit env-var path |

## User Stories

### Linux Desktop User

**As a Linux desktop developer**, I want `llm-svc` to reuse my Copilot CLI sign-in, so that I can run the proxy without manually exporting a token every session.

**Acceptance Criteria:**

- If `COPILOT_TOKEN` is unset and a Secret Service Copilot credential exists, startup succeeds in an authenticated state.
- If multiple GitHub.com credentials exist, the service prefers the account referenced by Copilot CLI metadata when available.
- If the keyring is unavailable or locked, the service degrades cleanly instead of crashing or hanging.

### Headless or Container Operator

**As an operator running `llm-svc` without a desktop keyring**, I want the service to keep using explicit env-var auth, so that server/container behavior stays predictable.

**Acceptance Criteria:**

- `COPILOT_TOKEN` continues to work on every platform and always wins over secure-store lookup.
- Missing D-Bus session or Secret Service availability results in the same unauthenticated/degraded mode the service already uses.
- The README explains that headless/container Linux should use explicit env-var configuration.

### Existing Windows User

**As a Windows user**, I want the current secure-store behavior to remain unchanged, so that Linux support does not regress the working desktop path.

**Acceptance Criteria:**

- Windows lookup still uses Windows Credential Manager.
- Token validation, 401 reload, and `/health` behavior remain unchanged.
- No API endpoints or request-routing behavior change as part of this feature.

## Functional Requirements

### FR-1: Auth Source Resolution

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | `COPILOT_TOKEN` must remain the highest-priority credential source on all platforms. |
| FR-1.2 | If `COPILOT_TOKEN` is absent, `CopilotClient` must consult an injected platform credential store. |
| FR-1.3 | If no credential is found, service startup must remain degraded rather than failing the process. |

### FR-2: Linux Secret Service Lookup

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | Linux lookup must query Secret Service over D-Bus instead of parsing secrets from `~/.copilot/config.json`. |
| FR-2.2 | Lookup must target the Copilot CLI storage shape: `service = copilot-cli` and GitHub.com accounts shaped like `https://github.com:<login>`. |
| FR-2.3 | Locked or unavailable Secret Service sessions must produce a null result, not a fatal startup error. |

### FR-3: Account Selection

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | On Linux, the service must prefer the account named by Copilot CLI `last_logged_in_user` metadata when that metadata is present. |
| FR-3.2 | If login metadata is missing or unusable, the service must fall back to the first GitHub.com Secret Service credential in deterministic order. |
| FR-3.3 | Only non-secret CLI metadata may be read from `~/.copilot/config.json`. |

### FR-4: Windows and Unsupported Platforms

| Requirement | Description |
|-------------|-------------|
| FR-4.1 | Windows behavior must remain functionally identical to the current Credential Manager lookup. |
| FR-4.2 | Unsupported platforms must register a no-op credential store and rely on `COPILOT_TOKEN`. |

### FR-5: Token Lifecycle

| Requirement | Description |
|-------------|-------------|
| FR-5.1 | Startup loading, background validation, and 401-triggered reload must all use the same credential-resolution order. |
| FR-5.2 | Existing auth log events must continue to describe successful load, missing credentials, validation failure, and expiry/reload attempts. |

### FR-6: Documentation and Test Coverage

| Requirement | Description |
|-------------|-------------|
| FR-6.1 | `README.md` must document Windows, Linux desktop, and headless/container auth expectations. |
| FR-6.2 | Unit tests must cover env-var precedence, Windows fallback, Linux metadata preference, and no-session-bus failure paths. |

## Non-Functional Requirements

### Performance

| Requirement | Target |
|-------------|--------|
| NFR-P1 | Credential lookup must happen only at startup or explicit reload points, not on every successful request |
| NFR-P2 | Linux lookup failure for unavailable D-Bus or keyring must fail fast without retry loops in the request path |

### Security

| Requirement | Target |
|-------------|--------|
| NFR-S1 | Secrets must come only from `COPILOT_TOKEN`, Windows Credential Manager, or Linux Secret Service |
| NFR-S2 | Full tokens must never be written to logs or planning artifacts |
| NFR-S3 | The feature must not introduce plaintext token parsing from `~/.copilot/config.json` |

### Reliability and Operability

| Requirement | Target |
|-------------|--------|
| NFR-R1 | `/health` behavior must remain consistent with current authenticated vs degraded semantics |
| NFR-R2 | Headless/container Linux must remain operable through explicit env-var configuration |
| NFR-R3 | CI-safe test coverage must remain runnable through existing `llm-svc.Tests` commands |

## Scope

### In Scope

- Linux Secret Service lookup for Copilot CLI credentials
- Infrastructure-only credential-store abstraction
- Copilot CLI metadata parsing for account preference only
- Program registration, unit tests, and README updates needed for the feature

### Out of Scope

- Parsing `~/.copilot/config.json` for primary token retrieval
- macOS keychain support
- Changes to API endpoints, request translation, or model routing
- Prompting the user to unlock a Linux keyring from the service process

### Future Considerations

- macOS keychain integration
- Configurable host support for GitHub Enterprise accounts if the CLI expands beyond `https://github.com`
- Additional structured logging for credential-source diagnostics

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Linux desktop startup | Authenticated without manual token export when Copilot CLI credential exists | Unit coverage plus manual verification on Linux desktop |
| Windows stability | No regression in Windows credential lookup path | Existing and extended service tests |
| Headless behavior | Clean degraded startup without crash when no keyring/session bus is available | Linux-store unit tests and README guidance |
| Documentation clarity | README distinguishes desktop secure-store and env-var fallback modes | Doc review against acceptance criteria |

## Assumptions

1. Copilot CLI continues storing Linux desktop credentials under Secret Service service `copilot-cli`.
2. `~/.copilot/config.json` continues to expose non-secret login metadata such as `last_logged_in_user`.
3. Direct `Tmds.DBus.Protocol` usage remains viable in `net10.0` without introducing native `libsecret` interop.

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Secret Service items exist for multiple accounts | Medium | Medium | Prefer `last_logged_in_user`, then deterministic GitHub.com fallback |
| D-Bus session is missing in containers/headless shells | High | Medium | Return null from Linux store and rely on env-var fallback |
| Direct D-Bus integration increases implementation complexity | Medium | Medium | Keep the store narrow, isolate protocol code, and test it independently |
| Windows behavior regresses during refactor | Low | High | Keep existing `CredentialManager` helper and wrap it with a Windows store rather than rewriting lookup logic |

## Glossary

| Term | Definition |
|------|------------|
| Secret Service | The freedesktop.org D-Bus API used by GNOME Keyring, KWallet, and similar Linux secret stores |
| Copilot CLI metadata | Non-secret fields in `~/.copilot/config.json` that identify which user is currently selected |
| Degraded startup | The current mode where the service starts without credentials and returns unauthenticated health status |
