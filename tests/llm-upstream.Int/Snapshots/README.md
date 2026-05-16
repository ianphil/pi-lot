# Upstream API captures

These snapshots are living documentation of the Copilot upstream API as observed
by direct HTTP calls to `https://api.enterprise.githubcopilot.com`.

The capture tests intentionally bypass `LlmSdk`, `llm-svc`, `llm-agent`, and
`llm-cli` request abstractions. They use existing credential loading only to
obtain a bearer token, then record direct upstream requests and responses.

Raw capture data is authoritative. The harness redacts credential-like values,
but it does not normalize response IDs, timestamps, request IDs, token usage,
dated model revisions, safety identifiers, SSE event payloads, or unknown
fields.

To intentionally refresh these snapshots:

```bash
LLM_UPSTREAM_UPDATE_SNAPSHOTS=1 dotnet test tests/llm-upstream.Int/llm-upstream.Int.csproj --filter Category=Smoke
```

Review snapshot diffs like API documentation changes.
