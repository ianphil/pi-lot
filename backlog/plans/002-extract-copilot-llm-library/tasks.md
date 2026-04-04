# Extract CopilotLlm Library — Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Two Test Layers
| Layer | Purpose | When to Run |
|-------|---------|-------------|
| **Unit Tests** | Implementation TDD (Red-Green-Refactor) | During implementation |
| **Integration Tests** | Host wiring verification | After Phase 3 |

## User Story Mapping

| Story | spec.md Reference | Tasks |
|-------|-------------------|-------|
| Library Consumer | FR-1, FR-2 | Phase 1, Phase 2 |
| Proxy Host Maintainer | FR-3, FR-4 | Phase 3, Phase 4 |
| Package Publisher | FR-5 | Phase 5 |

## Dependencies

```
Phase 1 (Library Project) ──► Phase 2 (DI Extension) ──► Phase 3 (Rewire Host)
                                                              │
                                                              ▼
                                                         Phase 4 (Split Tests) ──► Phase 5 (Packaging)
```

## Phase 1: Create Library Project & Move Files

### Project Setup
- [x] T001 [IMPL] Create `CopilotLlm/CopilotLlm.csproj` as `Microsoft.NET.Sdk` class library targeting net10.0 with RootNamespace `CopilotLlm`, package metadata, and required NuGet dependencies (`M.E.DI.Abstractions`, `M.E.Logging.Abstractions`, `M.E.Http`, `Tmds.DBus.Protocol`)

### Move Core/
- [x] T002 [IMPL] Move `Core/Models/*.cs` (7 files) to `CopilotLlm/Core/Models/`, update namespace from `LlmSvc.Core.Models` to `CopilotLlm.Core.Models`
- [x] T003 [IMPL] Move `Core/Ports/*.cs` (5 files) to `CopilotLlm/Core/Ports/`, update namespace from `LlmSvc.Core.Ports` to `CopilotLlm.Core.Ports`
- [x] T004 [IMPL] Move `Core/Services/*.cs` (7 files) to `CopilotLlm/Core/Services/`, update namespace from `LlmSvc.Core.Services` to `CopilotLlm.Core.Services`
- [x] T005 [IMPL] Move `Core/LogEvents.cs` to `CopilotLlm/`, update namespace from `LlmSvc.Core` to `CopilotLlm`

### Move Infrastructure/
- [x] T006 [IMPL] Move all Infrastructure/ files EXCEPT `Worker.cs` (12 files) to `CopilotLlm/Infrastructure/`, update namespace from `LlmSvc.Infrastructure` to `CopilotLlm.Infrastructure`
- [x] T007 [IMPL] Move `Infrastructure/Worker.cs` to `Worker.cs` (project root), keep it referencing `CopilotLlm` namespaces

### Verify
- [x] T008 [IMPL] Update all internal `using` statements within moved files (e.g., `using LlmSvc.Core.Models` → `using CopilotLlm.Core.Models`), verify `dotnet build CopilotLlm/CopilotLlm.csproj` succeeds

## Phase 2: DI Extension Method

### Implementation
- [x] T009 [TEST] Write test for `AddCopilotLlm()`: verifies all expected services are registered (IAuthProvider, IModelProvider, IResponsesService, IChatCompletionsService, translators, ModelListService)
- [x] T010 [IMPL] Create `CopilotLlm/ServiceCollectionExtensions.cs` with `AddCopilotLlm()` method — extract DI logic from Program.cs (platform-conditional credential stores, CopilotClient singleton aliasing, all translator/service registrations)
- [x] T011 [TEST] Write test verifying `AddCopilotLlm()` does NOT register Worker or any IHostedService

## Phase 3: Rewire Host

### Simplify llm-svc
- [ ] T012 [IMPL] Update `llm-svc.csproj`: remove Core/Infrastructure source includes, add `<ProjectReference Include="../CopilotLlm/CopilotLlm.csproj" />`, keep `Microsoft.Extensions.Hosting.WindowsServices` for Worker/Windows Service, remove `Tmds.DBus.Protocol` (now in library)
- [ ] T013 [IMPL] Simplify `Program.cs`: replace all inline DI with `builder.Services.AddCopilotLlm()`, keep only Worker registration, Windows Service config, event log config, and endpoint mapping. Update all `using` to CopilotLlm namespaces
- [ ] T014 [IMPL] Update `Worker.cs` usings to reference `CopilotLlm` namespace for `IAuthProvider` and `LogEvents`
- [ ] T015 [IMPL] Remove now-empty `Core/` and `Infrastructure/` directories from llm-svc
- [ ] T016 [IMPL] Verify `dotnet build llm-svc.sln` succeeds (both projects)

## Phase 4: Split Tests

### Library Tests
- [ ] T017 [IMPL] Create `CopilotLlm.Tests/CopilotLlm.Tests.csproj` referencing `CopilotLlm.csproj`, xunit, M.NET.Test.Sdk
- [ ] T018 [IMPL] Move unit tests that test Core/ directly: `ResponsesServiceTests.cs`, `CopilotClientTests.cs`, `CopilotCliConfigMetadataReaderTests.cs`, `LinuxSecretServiceCredentialStoreTests.cs` → `CopilotLlm.Tests/Unit/`
- [ ] T019 [IMPL] Update moved test files: namespace and `using` statements from `LlmSvc` → `CopilotLlm`; add DI extension tests from Phase 2 to this project
- [ ] T020 [IMPL] Add `[assembly: InternalsVisibleTo("CopilotLlm.Tests")]` to library if any tests need internal type access

### Host Tests
- [ ] T021 [IMPL] Update `llm-svc.Tests/llm-svc.Tests.csproj`: add reference to `CopilotLlm.csproj` (for model types), update usings
- [ ] T022 [IMPL] Update `FakeModelProvider.cs`, `ResponsesWebApplicationFactory.cs`, and all integration/smoke test files: namespaces from `LlmSvc` → `CopilotLlm`
- [ ] T023 [IMPL] Verify `dotnet test` — all unit + integration tests pass across both test projects

### Solution File
- [ ] T024 [IMPL] Update `llm-svc.sln` to include `CopilotLlm/CopilotLlm.csproj` and `CopilotLlm.Tests/CopilotLlm.Tests.csproj`

## Phase 5: Packaging & Cleanup

### NuGet
- [ ] T025 [IMPL] Verify `dotnet pack CopilotLlm/CopilotLlm.csproj -c Release` produces valid .nupkg
- [ ] T026 [IMPL] Verify package contains expected assemblies and dependencies

### Cleanup
- [ ] T027 [IMPL] Delete the quick plan `backlog/plans/20260404-extract-copilot-llm-library.md` (superseded by this feature plan)
- [ ] T028 [IMPL] Update `CONTRIBUTING.md` to reflect new project structure (CopilotLlm library + llm-svc host)
- [ ] T029 [IMPL] Update `README.md` project structure section

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Library Project | T001-T008 | 0 | 8 |
| Phase 2: DI Extension | T009-T011 | 2 | 1 |
| Phase 3: Rewire Host | T012-T016 | 0 | 5 |
| Phase 4: Split Tests | T017-T024 | 0 | 8 |
| Phase 5: Packaging | T025-T029 | 0 | 5 |
| **Total** | **29** | **2** | **27** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build llm-svc.sln` compiles all projects
- [ ] `dotnet test` passes all unit + integration tests
- [ ] `dotnet pack CopilotLlm/CopilotLlm.csproj` produces valid .nupkg
- [ ] Smoke test against running proxy confirms identical behavior
