# Testing SDK Changes

Place tests in the project that owns the changed behavior:

| Behavior | Test project |
|---|---|
| SDK client, Core models/services, Infrastructure adapters | `tests/llm-sdk.Tests` |
| Fake/live SDK reference-consumer behavior | `tests/llm-sdk.Int` |
| Service host or HTTP endpoint behavior | `tests/llm-svc.Tests` or `tests/llm-svc.Int` |
| Agent behavior built on the SDK | `tests/llm-agent.Tests` or `tests/llm-agent.Int` |

For SDK unit changes, run:

```bash
dotnet test tests/llm-sdk.Tests/llm-sdk.Tests.csproj --no-restore
```

For SDK integration changes, pair deterministic fake coverage with a small live
Smoke test when real upstream compatibility matters:

```bash
dotnet test tests/llm-sdk.Int/llm-sdk.Int.csproj --filter "Category!=Smoke" --no-restore
dotnet test tests/llm-sdk.Int/llm-sdk.Int.csproj --filter "Category=Smoke" --no-restore
```

Do not put fakes, mocks, or stubs in `scripts/test-matrix.*`; that matrix is for
live end-to-end checks against real CLI/proxy or SDK paths.
