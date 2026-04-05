# Extract CopilotLlm Library — Analysis

## Executive Summary

| Pattern | Integration Point |
|---------|-------------------|
| Hexagonal (Ports & Adapters) | Core/Ports → Infrastructure adapters; unchanged, just crosses project boundary |
| Composition root | Program.cs wires Core + Infrastructure; becomes library consumer |
| Platform-conditional DI | OS-specific credential stores registered via factory; moves to library extension method |
| Singleton services | All services registered as singletons; DI extension preserves this |
| Test double injection | FakeModelProvider replaces ports in tests; unchanged pattern |

## Architecture Comparison

### Current Architecture

```
llm-svc.csproj (single project)
├── Program.cs              Composition root + endpoints
├── Core/
│   ├── Models/             DTOs (20+ types, BCL-only)
│   ├── Ports/              Interfaces (4 interfaces + 2 records)
│   └── Services/           Translation engine (7 classes)
├── Infrastructure/
│   ├── CopilotClient.cs    IAuthProvider + IModelProvider (singleton)
│   ├── Worker.cs           BackgroundService (token refresh)
│   ├── Credential stores   Windows/Linux/NoOp
│   └── Support types       Config reader, D-Bus client, constants
└── LogEvents.cs            Structured event IDs
```

Everything lives in one project. The dependency rule is enforced by convention (Core/ never `using` Infrastructure/) but nothing prevents a violation.

### Target Architecture

```
CopilotLlm.csproj (class library, NuGet package)
├── Core/
│   ├── Models/             Same DTOs, namespace: CopilotLlm.Core.Models
│   ├── Ports/              Same interfaces, namespace: CopilotLlm.Core.Ports
│   └── Services/           Same translators, namespace: CopilotLlm.Core.Services
├── Infrastructure/
│   ├── CopilotClient.cs    Same adapter
│   ├── Credential stores   Same platform-conditional stores
│   └── Support types       Same config reader, D-Bus client
├── LogEvents.cs            Same event IDs
└── ServiceCollectionExtensions.cs   DI registration entry point

llm-svc.csproj (web host, references CopilotLlm)
├── Program.cs              Composition root + endpoints (calls AddCopilotLlm())
└── Worker.cs               BackgroundService (hosting concern)
```

The dependency rule is now enforced by the compiler — Core/ is in a different assembly, Infrastructure/ can reference it but not the reverse.

## Pattern Mapping

### 1. DI Registration (Program.cs → ServiceCollectionExtensions)

**Current Implementation:**
Program.cs directly registers all services inline:
- Platform-conditional credential store factory (`OperatingSystem.IsWindows()`)
- CopilotClient as both `IAuthProvider` and `IModelProvider` (singleton)
- All translators, services, ModelListService
- Worker as hosted service (conditional on non-Testing environment)

**Target Evolution:**
Library provides `services.AddCopilotLlm()` that registers everything except Worker.
Worker registration stays in Program.cs. Testing-environment skip logic stays in Program.cs.

### 2. Namespace Migration

**Current:** `LlmSvc.Core.*`, `LlmSvc.Infrastructure`
**Target:** `CopilotLlm.Core.*`, `CopilotLlm.Infrastructure`

All internal references update. The root namespace in the library csproj handles this.
Test code and Program.cs update their `using` statements.

### 3. CopilotClient Dual-Interface Pattern

**Current:** CopilotClient implements both `IAuthProvider` and `IModelProvider`. A single instance is registered as singleton, then resolved via both interfaces.

**Target:** Same pattern, but the extension method handles the singleton-aliasing:
```csharp
services.AddSingleton<CopilotClient>();
services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<CopilotClient>());
services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<CopilotClient>());
```

## What Exists vs What's Needed

### Currently Built
| Component | Status | Notes |
|-----------|--------|-------|
| Core/Models (20+ DTOs) | ✅ | BCL-only, zero external deps |
| Core/Ports (4 interfaces) | ✅ | Clean abstractions |
| Core/Services (7 classes) | ✅ | Pure translation logic |
| Infrastructure/CopilotClient | ✅ | HTTP adapter, dual-interface |
| Infrastructure/Credential stores | ✅ | Windows/Linux/NoOp |
| Infrastructure/Worker | ✅ | Stays in host |
| LogEvents | ✅ | Structured event IDs |
| Test fakes | ✅ | FakeModelProvider |
| WebApplicationFactory | ✅ | Integration test host |

### Needed
| Component | Status | Source |
|-----------|--------|--------|
| CopilotLlm.csproj | ❌ | New class library project |
| ServiceCollectionExtensions | ❌ | Extract from Program.cs DI logic |
| NuGet packaging metadata | ❌ | PackageId, Description, RepositoryUrl |
| GitHub Packages CI workflow | ❌ | Publish on tag/release |
| CopilotLlm.Tests.csproj | ❌ | Split from llm-svc.Tests |

## Key Insights

### What Works Well
1. Core/ has zero external dependencies — it's pure BCL. Moving it is trivial.
2. The port/adapter boundary is clean and well-tested. No leaky abstractions.
3. All services are singletons with constructor injection — DI extension method is straightforward.
4. FakeModelProvider already implements the port interfaces — test pattern is unaffected.

### Gaps/Limitations
| Limitation | Solution |
|------------|----------|
| `Microsoft.Extensions.Hosting.WindowsServices` is a heavy dep just for `BackgroundService` base class | Worker stays in host; library only needs `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` |
| CopilotClient uses `IHttpClientFactory` (from hosting) | Library references `Microsoft.Extensions.Http` (lightweight, no hosting dependency) |
| Namespace rename (`LlmSvc` → `CopilotLlm`) is a broad find-replace | Mechanical but must cover test code, Program.cs, all `using` statements |
| `internal` types (CopilotCredentialConstants) need `InternalsVisibleTo` for test access, or become public | Evaluate per-type; constants can stay internal with `InternalsVisibleTo` to test project |
