# CopilotLlmClient SDK Surface — Research

**Date**: 2026-04-04
**Sources**: GitHub Issue #4, pi-ai reference analysis, Azure SDK Guidelines, Google AIP, .NET Framework Design Guidelines
**Plan Version**: plan.md

## Summary

All research was conducted during issue #4 (8 comments, fully resolved). This document captures the key findings and how they inform our implementation.

## Findings Applied to Design

### 1. One Obvious Entry Point (Azure SDK / Google AIP)

| Aspect | Guideline | Our Design | Status |
|--------|-----------|------------|--------|
| Single client class | Minimize top-level clients | `CopilotLlmClient` wraps 3 services | CONFORMANT |
| Minimum constructor args | Take only what's needed to connect | DI-resolved, no constructor args | CONFORMANT |

### 2. Hero Path Should Be Short (Google AIP-4232)

| Aspect | Guideline | Our Design | Status |
|--------|-----------|------------|--------|
| Flattened convenience overloads | SDK may add short overloads | `CreateResponseAsync(model, input)` | CONFORMANT |
| Full request object always available | Convenience is additive | `CreateResponseAsync(CreateResponseRequest)` | CONFORMANT |

### 3. Options Bags Over Signature Explosion (Azure SDK)

| Aspect | Guideline | Our Design | Status |
|--------|-----------|------------|--------|
| Options types for optional behavior | Don't grow parameter lists | `CopilotLlmOptions` for config | CONFORMANT |
| Required args positional | Options bag for the long tail | Model + input positional, rest via request object | CONFORMANT |

### 4. Streaming Return Type (pi-ai Reference)

| Aspect | pi-ai Pattern | Our Design | Status |
|--------|---------------|------------|--------|
| Typed discriminated events | `AssistantMessageEvent` union | `ResponseStreamEvent` union | CONFORMANT |
| Dual consumption | `.result()` for final, iterate for stream | Separate stream/non-stream methods | ADAPTED |

### 5. Convenience Helpers (pi-ai Reference, .NET Guidelines)

| Aspect | Guideline | Our Design | Status |
|--------|-----------|------------|--------|
| No behavior on DTOs | pi-ai: standalone functions | Extension methods (C# idiomatic) | ADAPTED |
| DTO purity | CONTRIBUTING.md: plain DTOs | `ResponseExtensions` class | CONFORMANT |

### 6. Auth on Client (pi-ai Reference)

| Aspect | pi-ai Pattern | Our Design | Status |
|--------|---------------|------------|--------|
| No health/auth status | Auth failure = error event | Throw exception on bad credentials | CONFORMANT |
| No managed credential state | API keys per-request | `CopilotClient` manages internally (Q1 resolved) | ADAPTED |

### 7. Package Strategy (pi-ai Reference)

| Aspect | pi-ai Pattern | Our Design | Status |
|--------|---------------|------------|--------|
| Single package | One npm package, sub-path exports | One NuGet package, namespace separation | CONFORMANT |

### 8. Model Constants (pi-ai Reference)

| Aspect | pi-ai Pattern | Our Design | Status |
|--------|---------------|------------|--------|
| Auto-generated model catalog | `models.generated.ts` | Deferred — accept plain strings | FUTURE |

## Open Questions from Issue #4 — Resolution Status

| # | Question | Resolution | Source |
|---|----------|------------|--------|
| Q1 | Auth lifecycle without host | `CopilotClient` manages internally | PR #6, lib-v0.2.0 |
| Q2 | Streaming return type | Typed discriminated `ResponseStreamEvent` | Issue #4 comment |
| Q3 | `GetOutputText()` pattern | Extension methods | Issue #4 comment |
| Q4 | Auth/health on client | No — throw on bad creds | Issue #4 comment |
| Q5 | Options scope | `DefaultModel` + `HttpTimeout` only | Issue #4 comment |
| Q6 | InternalsVisibleTo | Public namespaces instead | Issue #4 comment |
| Q7 | One NuGet package | Yes — namespace separation only | Issue #4 comment |
| Q8 | Model constants | Deferred | Issue #4 comment |

## Conclusion

All design decisions are resolved. No further research needed. Proceed to implementation planning.
