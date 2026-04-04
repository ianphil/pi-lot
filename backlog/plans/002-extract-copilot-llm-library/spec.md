# Specification: Extract CopilotLlm Library

## Overview

### Problem Statement

The Copilot LLM translation engine, credential resolution, and HTTP client are locked inside a web host project. Anyone who wants Copilot LLM access must run the proxy — there's no way to embed the capability directly into a CLI tool, desktop app, or serverless function. The domain logic (Core/) and infrastructure adapters are architecturally clean but physically coupled to the ASP.NET host.

### Solution Summary

Extract Core/ and Infrastructure/ (minus Worker) into a standalone .NET class library, published as a NuGet package to GitHub Packages. The library provides a single `AddCopilotLlm()` extension method for DI integration. llm-svc becomes a thin ASP.NET host that references the library.

### Business Value
| Benefit | Impact |
|---------|--------|
| Reusability | Any .NET app can embed Copilot LLM access without running the proxy |
| Compiler-enforced boundaries | Dependency rule violations become build errors, not convention violations |
| Independent versioning | Library and host evolve on separate cadences |
| Publishable artifact | GitHub Packages distribution to other internal projects |

## User Stories

### Library Consumer
**As a .NET developer**, I want to add the CopilotLlm NuGet package to my project and call `services.AddCopilotLlm()`, so that I can send requests to Copilot's LLM API without running the proxy.

**Acceptance Criteria:**
- Package installs from GitHub Packages NuGet source
- `AddCopilotLlm()` registers all required services (auth, model provider, translators)
- Consumer can resolve `IResponsesService` and `IChatCompletionsService` from DI
- Consumer can resolve `IAuthProvider` to manage authentication lifecycle
- Platform-specific credential stores are auto-selected

### Proxy Host Maintainer
**As the llm-svc maintainer**, I want llm-svc to reference the library instead of containing the source, so that the host stays thin and focused on HTTP concerns.

**Acceptance Criteria:**
- Program.cs calls `AddCopilotLlm()` and maps endpoints — no inline service registration
- Worker.cs remains in the host project
- All existing tests pass without behavior changes
- Existing API surface is identical (same endpoints, same responses)

## Functional Requirements

### FR-1: Library Project Structure
| Requirement | Description |
|-------------|-------------|
| FR-1.1 | Class library targeting net10.0 with `Microsoft.NET.Sdk` |
| FR-1.2 | Contains Core/ (Models, Ports, Services) and Infrastructure/ (minus Worker) |
| FR-1.3 | Root namespace: `CopilotLlm` |
| FR-1.4 | NuGet packaging metadata (PackageId, Description, RepositoryUrl) |

### FR-2: DI Extension Method
| Requirement | Description |
|-------------|-------------|
| FR-2.1 | `IServiceCollection.AddCopilotLlm()` registers all services as singletons |
| FR-2.2 | Platform-conditional credential store selection (Windows/Linux/NoOp) |
| FR-2.3 | CopilotClient registered once, aliased to both IAuthProvider and IModelProvider |
| FR-2.4 | All translators and services registered |

### FR-3: Host Simplification
| Requirement | Description |
|-------------|-------------|
| FR-3.1 | llm-svc.csproj references CopilotLlm via ProjectReference |
| FR-3.2 | Program.cs reduced to: AddCopilotLlm() + endpoint mapping + Worker registration |
| FR-3.3 | Worker.cs remains in llm-svc, references IAuthProvider from library |

### FR-4: Test Reorganization
| Requirement | Description |
|-------------|-------------|
| FR-4.1 | Unit tests that test Core/ logic directly move to CopilotLlm.Tests |
| FR-4.2 | Integration tests (WebApplicationFactory) stay in llm-svc.Tests |
| FR-4.3 | FakeModelProvider stays in llm-svc.Tests (it's a test concern for the host) |
| FR-4.4 | All existing tests pass with zero behavior changes |

### FR-5: GitHub Packages Publishing
| Requirement | Description |
|-------------|-------------|
| FR-5.1 | csproj configured with PackageId, Authors, Description, RepositoryUrl |
| FR-5.2 | `dotnet pack` produces a valid .nupkg |
| FR-5.3 | Publishable to GitHub Packages NuGet feed |

## Non-Functional Requirements

### Performance
| Requirement | Target |
|-------------|--------|
| No runtime overhead | Library extraction is compile-time only; zero runtime cost |

### Compatibility
| Requirement | Target |
|-------------|--------|
| API backward compatibility | All existing proxy endpoints unchanged |
| Test backward compatibility | All existing tests pass |

## Scope

### In Scope
- Create CopilotLlm class library project
- Move Core/ and Infrastructure/ (minus Worker) into library
- Rename namespaces from LlmSvc to CopilotLlm
- Create AddCopilotLlm() DI extension
- Simplify Program.cs to use library
- Reorganize tests
- Add NuGet packaging metadata
- Update solution file

### Out of Scope
- Modifying llm-cli (future: could embed library directly)
- Adding new features or capabilities
- Changing the proxy's API surface
- Multi-provider support
- CI/CD pipeline for automated publishing (can be added later)

### Future Considerations
- llm-cli embedding the library for proxy-free operation
- CopilotLlm.Core / CopilotLlm.Infrastructure package split if dependency size becomes a concern
- Automated CI publishing to GitHub Packages on release tags

## Success Criteria
| Metric | Target | Measurement |
|--------|--------|-------------|
| Build | Both projects compile | `dotnet build llm-svc.sln` |
| Tests | All pass | `dotnet test` with no regressions |
| Pack | NuGet produces valid package | `dotnet pack CopilotLlm.csproj` |
| Host size | Program.cs ≤ 100 lines | Line count |
| API compat | Zero endpoint changes | Smoke tests pass |

## Assumptions
1. GitHub Packages NuGet feed is available for the repository owner
2. Consumers will add the GitHub NuGet source to their NuGet.config
3. .NET 10 SDK is available for all consumers

## Risks and Mitigations
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Namespace rename breaks external references | Low | Medium | llm-cli uses HTTP, not project references; no external consumers yet |
| InternalsVisibleTo needed for test access | Medium | Low | Add attribute for CopilotLlm.Tests; evaluate making types public |
| Platform-specific code in shared library | Low | Low | Already guarded at runtime; credential stores self-select |
