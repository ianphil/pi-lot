---
description: 'Maintain direct upstream Copilot API capture contracts without normalizing drift.'
applyTo: 'tests/llm-upstream.Int/**/*'
---

# Upstream Capture Tests

`tests/llm-upstream.Int` contains direct Copilot upstream capture contracts.
These tests bypass `LlmSdk`, `llm-svc`, `llm-agent`, and `llm-cli` request
abstractions. They use existing credential loading only to obtain a token, then
call `https://api.enterprise.githubcopilot.com` directly.

Committed snapshots are living upstream capability documentation. Capture
advertised positive surfaces and useful negative probes. Redact secrets and
credential-like values only.

Do not normalize IDs, timestamps, model revisions, usage counts, response
headers, SSE payloads, websocket messages, unknown fields, or other upstream
details just because this repo does not consume them yet.

All upstream capture tests are `Category=UpstreamCapture`, not `Smoke`. Refresh
snapshots only when intentionally documenting accepted upstream drift:

```powershell
$env:LLM_UPSTREAM_UPDATE_SNAPSHOTS = "1"
dotnet test tests\llm-upstream.Int --filter "Category=UpstreamCapture"
```
