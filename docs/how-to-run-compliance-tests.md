# How to Run Open Responses Compliance Tests

The [Open Responses](https://openresponses.org) project publishes a compliance test
suite that validates a server's `/responses` endpoint against the official
OpenAPI-derived Zod schemas. This document explains how to run those tests
against `llm-svc` and captures lessons learned from our first compliance pass.

## Prerequisites

| Tool | Purpose |
|------|---------|
| [Bun](https://bun.sh) runtime | Required by the compliance test runner |
| `C:\src\openresponses` clone | The [openresponses](https://github.com/openresponses/openresponses) repo |
| `llm-svc` running on `:5100` | The proxy under test |

Install Bun (if needed):

```powershell
npm install -g bun
```

Install the openresponses project dependencies (one-time):

```powershell
cd C:\src\openresponses
bun install
```

## Running the Tests

Start the proxy from source (or use the published build):

```powershell
cd C:\src\llm-svc
dotnet run --no-launch-profile
```

In a second terminal, run the compliance suite:

```powershell
cd C:\src\openresponses
bun run test:compliance --base-url http://localhost:5100/v1 --api-key unused --model gpt-5.4-mini
```

The `--api-key unused` flag works because `llm-svc` does not enforce client
auth (out-of-scope for the local-proxy product shape).

### Filtering to a single test

```powershell
bun run test:compliance --base-url http://localhost:5100/v1 --api-key unused --model gpt-5.4-mini --filter basic-response
```

### Verbose output (shows full request/response on failure)

```powershell
bun run test:compliance --base-url http://localhost:5100/v1 --api-key unused --model gpt-5.4-mini --verbose
```

### JSON output (for CI integration)

```powershell
bun run test:compliance --base-url http://localhost:5100/v1 --api-key unused --model gpt-5.4-mini --json > results.json
```

## Available Test IDs

| Test ID | What it validates |
|---------|-------------------|
| `basic-response` | Simple text request → validates full `ResponseResource` schema |
| `streaming-response` | SSE stream → validates each event against streaming event schemas |
| `system-prompt` | System role in input |
| `tool-calling` | Function tool → expects `function_call` output item |
| `image-input` | Image URL in user content |
| `multi-turn` | Conversation history round-trip |

## Model Selection

**Use `gpt-5.4-mini` for compliance tests.** This is a fast, cheap model
available through GitHub Copilot that produces clean responses.

### Why not `gpt-5-mini`?

`gpt-5-mini` returns a phantom empty-content message item (`content: []`)
before the real response in native `/responses` mode. This extra item fails
the Zod item schema validation. The root cause is upstream (GitHub Copilot
API), not our proxy.

### Testing translated (chat completions) models

Models that only support `/chat/completions` (e.g., `claude-haiku-4.5`) take
a different code path through our translator. Running compliance tests
against these models exercises the translation layer rather than native
passthrough:

```powershell
bun run test:compliance --base-url http://localhost:5100/v1 --api-key unused --model claude-haiku-4.5
```

This is valuable because it validates that our `ChatCompletionsTranslator`
produces a spec-compliant response envelope.

## Lessons Learned

### 1. The Zod schema is strict about every field

The compliance tests validate the **complete `ResponseResource` shape** —
not just behavioral correctness. Every field listed in the OpenAPI spec must
be present with the correct type. Fields the spec marks as required
(non-nullable) must have values; fields marked as nullable must still appear
in the JSON as `null`, not be omitted.

Our initial response only included ~12 fields. The spec requires **31 fields**
on every response.

### 2. `WhenWritingNull` fights the spec

Our JSON serializer uses `DefaultIgnoreCondition = WhenWritingNull` to keep
payloads lean. But the spec requires nullable fields like `completed_at`,
`instructions`, `error`, and `reasoning` to be present as `null`. We solved
this with per-property `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`
annotations on nullable fields that the spec requires.

### 3. Content parts require `logprobs`

The `output_text` content schema requires a `logprobs` array (can be empty).
This was not obvious from reading the spec prose but showed up immediately
in the Zod validation.

### 4. Tool definitions require `strict`

The `strict` field on function tool definitions must be serialized even when
null. Same `WhenWritingNull` issue — solved with the `JsonIgnoreCondition.Never`
attribute.

### 5. Windows `\r\n` breaks SSE parsing

`StringBuilder.AppendLine()` emits `\r\n` on Windows. SSE parsers expect
`\n` line endings. When our proxy re-emits upstream SSE chunks through
`AppendLine()`, the extra `\r` causes event boundary detection to fail.
The fix: use `.Append(line).Append('\n')` instead of `.AppendLine(line)`.

### 6. Native streaming passthrough needs light normalization

For models that support `/responses` natively, our proxy passes upstream
SSE chunks through without translation. But the upstream may omit fields
the spec requires (e.g., `prompt_cache_key`). We inject missing nullable
fields into the passthrough stream to satisfy the schema.

### 7. Upstream returns phantom empty items

Some models (e.g., `gpt-5-mini`) return an extra message item with
`content: []` before the actual response. Our `NormalizeNativeResponse`
method filters these out, but the `TryDeserializeCanonical` path can fail
when the upstream response contains null values for our non-nullable C#
properties (like `double Temperature`). Both the canonical and manual
mapping paths need the empty-item filter.

### 8. Streaming events also require `logprobs`

The `logprobs` field requirement isn't limited to content parts in the
response envelope — it's also required on streaming events:
`response.content_part.added`, `response.content_part.done`,
`response.output_text.delta`, and `response.output_text.done`. An empty
array `[]` satisfies the schema.

## Architecture: Two Code Paths

Understanding which code path runs for a given model is critical for
debugging compliance failures:

```
Request → Model supports /responses?
  ├─ YES (native) → Raw passthrough (streaming) or NormalizeNativeResponse (non-streaming)
  └─ NO (translated) → ChatCompletionsTranslator.ToResponse / StreamTranslator
```

Native models: `gpt-5.4-mini`, `gpt-5-mini`, `gpt-5.1`, `gpt-5.2`, etc.
Translated models: `claude-haiku-4.5`, `claude-sonnet-4.5`, `claude-opus-4.6`, etc.

Compliance tests should ideally pass on **both** paths. As of v0.4.0,
**both** `gpt-5.4-mini` (native) and `claude-haiku-4.5` (translated) pass
all 6 compliance tests.
