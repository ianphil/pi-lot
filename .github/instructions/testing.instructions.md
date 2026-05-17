---
description: 'Place tests in the owning project and keep unit tests focused.'
applyTo: 'tests/**/*.cs'
---

# Testing Ownership

Test the package that owns the behavior, not the first consumer where the
behavior was noticed.

| Changed surface | Test project |
|---|---|
| SDK client, Core models/services, Infrastructure adapters | `tests/llm-sdk.Tests/` |
| SDK fake/live reference-consumer behavior | `tests/llm-sdk.Int/` |
| Service host, HTTP endpoints, service wiring | `tests/llm-svc.Tests/` |
| Service fake/live proxy behavior | `tests/llm-svc.Int/` |
| CLI commands, CLI agents, local tools | `tests/llm-cli.Tests/` |
| Agent loop, events, context budget | `tests/llm-agent.Tests/` |
| Agent fake/live behavior against SDK client surface | `tests/llm-agent.Int/` |
| Browser UI behavior | `tests/llm-ui.Tests/` |
| Upstream Copilot API captures and drift detection | `tests/llm-upstream.Int/` |

Test doubles belong in the owning test project, usually under `Fakes/`. Keep
helpers internal to the owning test project unless the repo explicitly adopts a
shared test-support project.

Name xUnit tests using `MethodName_Scenario_ExpectedBehavior`. Use focused
assertions and avoid `// Arrange`, `// Act`, and `// Assert` comments.
