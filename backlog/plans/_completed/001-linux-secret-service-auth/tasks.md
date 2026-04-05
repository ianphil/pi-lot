# Linux Secret Service Auth Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Two Test Layers

| Layer | Purpose | When to Run |
|-------|---------|-------------|
| **Unit + Integration Tests** | Implementation TDD for credential resolution, DI wiring, degraded auth behavior, and docs-adjacent coverage where practical | During implementation and before merge |

## User Story Mapping

| Story | spec.md Reference | Primary validation |
|-------|-------------------|--------------------|
| Linux desktop credential reuse | Linux Desktop User, FR-2, FR-3 | Linux store unit tests plus manual desktop verification |
| Headless/container fallback | Headless or Container Operator, FR-1, FR-4 | Linux store failure-path tests and degraded startup checks |
| Windows stability | Existing Windows User, FR-4, FR-5 | CopilotClient unit tests and retained Windows helper behavior |

## Dependencies

```text
Phase 1: Credential-store refactor
    |
    v
Phase 2: Linux Secret Service + metadata selection
    |
    v
Phase 3: Wiring, docs, and acceptance verification
```

## Phase 1: Credential-Store Refactor

### Test-First Refactor

- [x] T001 [TEST] Extend `llm-svc.Tests/Unit/CopilotClientTests.cs` to cover env-var precedence over an injected store
- [x] T002 [TEST] Extend `llm-svc.Tests/Unit/CopilotClientTests.cs` to cover reload-after-401 using the injected store path
- [x] T003 [IMPL] Add `ICopilotCredentialStore`, `WindowsCredentialStore`, and `NoOpCopilotCredentialStore`; inject the store into `CopilotClient`
- [x] T004 [VERIFY] Review refactored auth loading to confirm env-var precedence remains the first branch

## Phase 2: Linux Secret Service Lookup

### Linux Credential Resolution

- [x] T005 [TEST] Add `LinuxSecretServiceCredentialStoreTests.cs` for preferred-account selection, deterministic fallback, and unavailable session bus handling
- [x] T006 [TEST] Add `CopilotCliConfigMetadataReaderTests.cs` for missing, malformed, and valid config metadata
- [x] T007 [IMPL] Implement `CopilotCliConfigMetadataReader` for `last_logged_in_user` and `logged_in_users`
- [x] T008 [IMPL] Implement `LinuxSecretServiceCredentialStore` on direct `Tmds.DBus.Protocol` with GitHub.com filtering and null-on-failure behavior
- [x] T009 [VERIFY] Manually review Linux account-selection and no-session-bus paths against the implemented tests and code

## Phase 3: Composition, Docs, and Final Verification

### Wiring and Documentation

- [x] T010 [TEST] Add or extend tests that prove startup/degraded behavior still works with the refactored auth provider wiring
- [x] T011 [IMPL] Register the OS-specific credential stores in `Program.cs` and add the D-Bus package reference in `llm-svc.csproj`
- [x] T012 [IMPL] Update `README.md` with Windows, Linux desktop, and headless/container auth guidance
- [x] T013 [VERIFY] Run CI-safe `llm-svc.Tests` coverage and perform a manual Linux desktop auth check before merge

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] | [VERIFY] |
|-------|-------|--------|--------|----------|
| Phase 1 | T001-T004 | 2 | 1 | 1 |
| Phase 2 | T005-T009 | 2 | 2 | 1 |
| Phase 3 | T010-T013 | 1 | 2 | 1 |
| **Total** | **13** | **5** | **5** | **3** |

## Final Validation

- [x] `dotnet test llm-svc.Tests --filter "Category!=Smoke" --no-restore` passes
- [x] Manual Linux desktop verification succeeds with Copilot CLI already signed in and `COPILOT_TOKEN` unset
- [x] README auth documentation matches the implemented credential-source order
