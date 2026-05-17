---
description: 'Use paired fake/live integration tests for SDK, service, and agent scenarios.'
applyTo: 'tests/*.Int/**/*.cs'
---

# Fake and Live Integration Tests

The `.Int` projects are reference-consumer integration suites. They replace the
old pattern of expanding `llm-cli` or `scripts/test-matrix.*` whenever SDK,
service, or agent behavior needs coverage.

Important integration scenarios should usually have two tests in the owning
`.Int` project:

| Test type | Category | Purpose |
|---|---|---|
| Fake integration | no `Smoke` trait | Deterministic correctness through the public consumer surface with scripted fakes |
| Live integration | `[Trait("Category", "Smoke")]` | Compatibility with real credentials, network, and upstream behavior |

Fake tests should use the same public surface as live tests and assert forwarded
requests, options, events, and responses. Put deterministic negative, edge-case,
and error-path coverage in fake tests.

Live tests should be small happy-path smoke/reference checks. Do not rely on
live tests to trigger validation failures, rare errors, or model-dependent
behavior.

For SDK behavior, prefer paired `llm-sdk.Int` tests. For agent behavior, prefer
paired `llm-agent.Int` tests. For service/proxy behavior, prefer paired
`llm-svc.Int` tests.
