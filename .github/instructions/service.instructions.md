---
description: 'Keep llm-svc a thin host and test service behavior in the owning projects.'
applyTo: 'src/llm-svc/**/*.cs,tests/llm-svc.Tests/**/*.cs,tests/llm-svc.Int/**/*.cs'
---

# Service Boundary

`llm-svc` is the deployable HTTP proxy host. Keep `src/llm-svc/Program.cs` thin:
call `AddLlmSdk()`, map endpoints, and register `Worker`. Reusable behavior
belongs in `src/llm-sdk/`.

Service/proxy HTTP contract changes belong in `tests/llm-svc.Tests` or
`tests/llm-svc.Int`, not in CLI tests. Use `tests/llm-svc.Tests` for host,
endpoint, and wiring behavior. Use `tests/llm-svc.Int` when behavior benefits
from fake/live proxy coverage against real host wiring.

Stop the scheduled task before building or testing service projects on Windows;
`llm-svc.exe` may be locked while the proxy is running.
