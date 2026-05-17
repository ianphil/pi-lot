---
name: fake-live-int-tests
description: Use this when adding or reviewing fake/live integration tests in this repo, especially for SDK, service, or agent behavior. It creates paired .Int coverage: fake tests for deterministic happy, negative, edge, and error cases; live Smoke tests for small real-upstream happy-path compatibility.
---

# Fake/Live Integration Test Workflow

Use this skill when a user asks for fake/live integration tests, `.Int` tests,
live smoke coverage, reference-consumer tests, or integration coverage for SDK,
service, or agent behavior.

Always read `CONTRIBUTING.md` and these scoped instructions before editing:

- `.github/instructions/testing.instructions.md`
- `.github/instructions/integration-tests.instructions.md`
- the surface-specific instruction file for the changed code, such as
  `sdk-client.instructions.md`, `service.instructions.md`, or
  `agent.instructions.md`

## Principle

Important integration scenarios should usually be paired:

| Test | Category | Role |
|---|---|---|
| Fake integration | no `Smoke` trait | Deterministic correctness through the same public surface as the live test |
| Live integration | `[Trait("Category", "Smoke")]` | Small happy-path compatibility check with real credentials, network, and upstream behavior |

Fake tests carry the behavioral depth. Live tests prove the real external path
still works.

## Step 1 - Pick the owning `.Int` project

Use the project that owns the public surface:

| Behavior | Project |
|---|---|
| SDK client or SDK reference-consumer flow | `tests/llm-sdk.Int` |
| Agent loop as a public SDK consumer | `tests/llm-agent.Int` |
| Service/proxy HTTP behavior against host wiring | `tests/llm-svc.Int` |

Do not expand `llm-cli` tests or `scripts/test-matrix.*` to prove SDK, service,
or agent internals. The CLI matrix is for real user-facing CLI/proxy E2E smoke
only.

## Step 2 - Define the shared scenario

Write one concise scenario statement before adding tests:

- Public surface under test.
- Request shape or user action.
- Expected response, event, option forwarding, or error behavior.
- Which assertions belong only in the fake test because live upstream cannot
  reliably produce them.

The fake and live tests should exercise the same public surface, even when the
fake version asserts more details.

## Step 3 - Write the fake integration tests

Fake tests are PR-safe and deterministic. Use scripted fakes in the owning test
project, usually under `Fakes/`.

Cover:

1. Happy path through the real public surface.
2. Negative or validation cases.
3. Edge cases that are hard to force live.
4. Error paths such as malformed upstream data, failed responses, cancellation,
   or partial streams when relevant.
5. Forwarded requests, options, events, and responses.

Do not mark fake integration tests with `Category=Smoke`.

## Step 4 - Write the live smoke test

Live tests should be small reference checks:

- Mark with `[Trait("Category", "Smoke")]`.
- Use real credentials, network, and upstream behavior.
- Prefer happy-path assertions that are stable across model output variance.
- Do not depend on rare errors, validation failures, exact natural-language
  output, timing, or specific token counts.

The live test proves compatibility, not exhaustive correctness.

## Step 5 - Validate with the right commands

Run fake integration tests first:

```powershell
dotnet test tests\llm-sdk.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-agent.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-svc.Int --filter "Category!=Smoke" --no-restore
```

Run the matching live smoke only when credentials and network are available:

```powershell
dotnet test tests\llm-sdk.Int --filter "Category=Smoke" --no-restore
dotnet test tests\llm-agent.Int --filter "Category=Smoke" --no-restore
dotnet test tests\llm-svc.Int --filter "Category=Smoke" --no-restore
```

Use only the command for the owning project unless a broader validation is
needed.

## Step 6 - Handoff

Summarize the pair explicitly:

- Fake test: what deterministic happy/negative/error behavior it proves.
- Live smoke: what real-upstream happy path it proves.
- Any live validation skipped because credentials, network, or proxy setup was
  unavailable.
