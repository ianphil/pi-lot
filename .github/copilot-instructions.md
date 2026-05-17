# Copilot Instructions for llm-svc

Start every session by reading `CONTRIBUTING.md`. It is the contributor-facing
source for repository workflow, build and test commands, versioning, release
rules, and available AI tooling.

Also obey the scoped instructions in `.github/instructions/*.instructions.md`.
Those files are the authoritative code-generation and review rules for specific
paths and surfaces.

## Always-Loaded Architecture Rule

This repo follows a ports-and-adapters dependency direction:

```text
llm-sdk/Infrastructure -> llm-sdk/Core <- llm-sdk/Client
                                 ^
                     ServiceCollectionExtensions
                                 ^
                         src/llm-svc/Program.cs
```

`src/llm-sdk/Core` and `src/llm-sdk/Proxy` must not depend on Infrastructure,
HTTP clients, host frameworks, or deployable projects. Define abstractions at the
Core/Proxy boundary and implement external details in Infrastructure.

`llm-cli`, `llm-agent`, `llm-ui`, and `llm-svc` are separate consumers. Do not
move behavior across package boundaries just because another package may use it
later. Implement shared semantics below the boundary only when multiple public
surfaces actually consume the same behavior.

## Always-Loaded Build Constraint

The proxy often runs as a Windows Scheduled Task and locks `llm-svc.exe`. Do not
build or test the full solution, `src/llm-svc`, or `tests/llm-svc.Tests` while
the task is running. For library-only work, target `src/llm-sdk`,
`tests/llm-sdk.Tests`, and `tests/llm-sdk.Int` directly.
