# Plan: Extract CopilotLlm Library

## Summary

Extract Core/ and Infrastructure/ (minus Worker) from llm-svc into a standalone CopilotLlm class library. The library provides `AddCopilotLlm()` for DI integration. llm-svc becomes a thin ASP.NET host. Published to GitHub Packages as a NuGet.

## Architecture

```
Before:
┌─────────────────────────────────────────┐
│ llm-svc.csproj (Web SDK)                │
│ ├── Program.cs (composition + endpoints)│
│ ├── Core/     (domain logic)            │
│ ├── Infrastructure/ (adapters + Worker) │
│ └── LogEvents.cs                        │
└─────────────────────────────────────────┘

After:
┌──────────────────────────────────┐     ┌──────────────────────────────┐
│ CopilotLlm.csproj (Class Lib)   │     │ llm-svc.csproj (Web Host)    │
│ ├── Core/                        │◄────│ ├── Program.cs               │
│ │   ├── Models/                  │     │ │   calls AddCopilotLlm()    │
│ │   ├── Ports/                   │     │ │   maps endpoints           │
│ │   └── Services/                │     │ └── Worker.cs                │
│ ├── Infrastructure/              │     │     (BackgroundService)      │
│ │   ├── CopilotClient.cs         │     └──────────────────────────────┘
│ │   ├── Credential stores        │
│ │   └── Support types            │
│ ├── LogEvents.cs                 │
│ └── ServiceCollectionExtensions  │
└──────────────────────────────────┘
```

## Detailed Architecture

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| CopilotLlm (library) | Domain logic, translation, HTTP client, auth | Any .NET host via DI |
| ServiceCollectionExtensions | DI registration entry point | Microsoft.Extensions.DI |
| llm-svc (host) | HTTP endpoints, Worker lifecycle | CopilotLlm library |
| Worker.cs | Token refresh scheduling | IAuthProvider from library |

### Data Flow: Request Through Library

```
Consumer App (or llm-svc endpoints)
  │
  ▼
IResponsesService.CreateAsync(request)
  │
  ├─ model supports /responses? ──→ IModelProvider.SendResponsesAsync()
  │                                   └─ CopilotClient → Copilot API
  │
  └─ chat-only model? ──────────→ ChatCompletionsTranslator.ToChat()
                                    └─ IModelProvider.SendChatCompletionsAsync()
                                      └─ CopilotClient → Copilot API
                                        └─ ChatCompletionsTranslator.ToResponse()
```

## File Structure

```
copilot-llm-svc/
├── CopilotLlm/                                    # NEW: library project
│   ├── CopilotLlm.csproj                          # NEW: class library
│   ├── ServiceCollectionExtensions.cs              # NEW: AddCopilotLlm()
│   ├── LogEvents.cs                                # MOVE from root
│   ├── Core/
│   │   ├── Models/
│   │   │   ├── ChatCompletionModels.cs             # MOVE
│   │   │   ├── CopilotApiModels.cs                 # MOVE
│   │   │   ├── ErrorTypes.cs                       # MOVE
│   │   │   ├── JsonElementHelpers.cs               # MOVE
│   │   │   ├── OpenAIModels.cs                     # MOVE
│   │   │   ├── ResponsesApiModels.cs               # MOVE
│   │   │   └── ResponsesDeserializationModels.cs   # MOVE
│   │   ├── Ports/
│   │   │   ├── IAuthProvider.cs                    # MOVE
│   │   │   ├── IChatCompletionsService.cs          # MOVE
│   │   │   ├── IModelProvider.cs                   # MOVE
│   │   │   ├── IResponsesService.cs                # MOVE
│   │   │   └── ResponseHttpResult.cs               # MOVE
│   │   └── Services/
│   │       ├── ChatCompletionsService.cs           # MOVE
│   │       ├── ChatCompletionsStreamTranslator.cs  # MOVE
│   │       ├── ChatCompletionsTranslator.cs        # MOVE
│   │       ├── ModelListService.cs                 # MOVE
│   │       ├── ResponseSseSerializer.cs            # MOVE
│   │       ├── ResponsesService.cs                 # MOVE
│   │       └── ResponsesStreamToChatTranslator.cs  # MOVE
│   └── Infrastructure/
│       ├── CopilotClient.cs                        # MOVE
│       ├── CopilotCliConfigMetadataReader.cs       # MOVE
│       ├── CopilotCliLoginMetadata.cs              # MOVE
│       ├── CopilotCredentialConstants.cs            # MOVE
│       ├── CredentialManager.cs                     # MOVE
│       ├── ICopilotCredentialStore.cs               # MOVE
│       ├── ISecretServiceClient.cs                  # MOVE
│       ├── LinuxSecretServiceCredentialStore.cs      # MOVE
│       ├── NoOpCopilotCredentialStore.cs             # MOVE
│       ├── SecretServiceDbusClient.cs                # MOVE
│       ├── SecretServiceItem.cs                      # MOVE
│       └── WindowsCredentialStore.cs                 # MOVE
├── Program.cs                                       # MODIFY: simplify
├── Worker.cs                                        # MOVE from Infrastructure/
├── llm-svc.csproj                                   # MODIFY: reference library
├── CopilotLlm.Tests/                               # NEW: library tests
│   ├── CopilotLlm.Tests.csproj                     # NEW
│   └── Unit/                                        # MOVE unit tests
│       ├── CopilotCliConfigMetadataReaderTests.cs
│       ├── CopilotClientTests.cs
│       ├── LinuxSecretServiceCredentialStoreTests.cs
│       └── ResponsesServiceTests.cs
├── llm-svc.Tests/                                   # MODIFY: keep integration + smoke
│   ├── Fakes/FakeModelProvider.cs                   # STAY
│   ├── Integration/                                 # STAY
│   └── Smoke/                                       # STAY
└── llm-svc.sln                                      # MODIFY: add new projects
```

## Critical: Namespace Rename

**Problem**: Current namespace is `LlmSvc.Core.*` / `LlmSvc.Infrastructure`. Renaming to `CopilotLlm.*` touches every file — all `namespace` declarations and all `using` statements across source and test code.

**Solution**: Mechanical find-replace in well-defined order:
1. Rename namespace declarations in moved files
2. Update `using` statements in Program.cs, Worker.cs, test files
3. Set `<RootNamespace>CopilotLlm</RootNamespace>` in library csproj
4. Verify build succeeds after each batch

## Implementation Phases

1. **Phase 1: Create library project** — csproj, move files, fix namespaces
2. **Phase 2: DI extension method** — ServiceCollectionExtensions.AddCopilotLlm()
3. **Phase 3: Rewire host** — simplify Program.cs, move Worker.cs
4. **Phase 4: Split tests** — library unit tests vs host integration tests
5. **Phase 5: Packaging** — NuGet metadata, dotnet pack verification

Details in tasks.md.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Single package | One NuGet: CopilotLlm | Simpler; platform-specific code self-guards at runtime |
| No hosting dependency | Library uses M.E.DI.Abstractions + M.E.Logging.Abstractions + M.E.Http | Any host can use it, not just ASP.NET |
| Worker stays in host | Worker.cs moves to llm-svc root | Token refresh schedule is a hosting concern |
| Namespace rename | LlmSvc → CopilotLlm | Package identity should match library name |
| ProjectReference for dev | llm-svc uses ProjectReference locally | PackageReference for external consumers |

## Files to Modify

| File | Change |
|------|--------|
| Program.cs | Replace inline DI with AddCopilotLlm(); update usings |
| llm-svc.csproj | Remove Core/Infra sources, add ProjectReference, keep WinExe + hosting deps |
| llm-svc.sln | Add CopilotLlm and CopilotLlm.Tests projects |
| llm-svc.Tests/llm-svc.Tests.csproj | Reference CopilotLlm.Tests or library directly; update usings |
| All test files | Update namespace usings from LlmSvc to CopilotLlm |

## New Files

| File | Purpose |
|------|---------|
| CopilotLlm/CopilotLlm.csproj | Class library project |
| CopilotLlm/ServiceCollectionExtensions.cs | AddCopilotLlm() DI extension |
| CopilotLlm.Tests/CopilotLlm.Tests.csproj | Library test project |
| Worker.cs (at llm-svc root) | Moved from Infrastructure/ |

## Verification

1. `dotnet build llm-svc.sln` — all projects compile
2. `dotnet test` — all unit + integration tests pass
3. `dotnet pack CopilotLlm/CopilotLlm.csproj` — produces valid .nupkg
4. Smoke test against running proxy — same behavior

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Namespace rename errors | Batch-rename with IDE/sed, build after each file group |
| Missing InternalsVisibleTo | Add `[InternalsVisibleTo("CopilotLlm.Tests")]` in library |
| Worker dependency on hosting | Worker stays in host; library has no hosting dep |
| Build breaks during migration | Phase approach — each phase produces a building solution |

## Limitations (MVP)

1. No CI/CD pipeline for automated package publishing (manual dotnet push)
2. No Source Link integration (can be added later)
3. llm-cli not updated to embed library (future phase)
