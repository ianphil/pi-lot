# API Reference

The Copilot LLM Proxy exposes an OpenAI-compatible Responses API on
`http://localhost:5100`. No API key is required — the proxy handles
authentication via Copilot CLI credentials in Windows Credential Manager.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check |
| GET | `/v1/models` | List available models |
| POST | `/v1/responses` | Create a response |
| POST | `/v1/chat/completions` | Chat completions (OpenAI-compatible) |

Short paths without the `/v1` prefix also work (`/models`, `/responses`,
`/chat/completions`).

---

## POST /v1/responses

### Request body

```json
{
  "model": "gpt-5.4-mini",
  "input": "What is the capital of France?",
  "stream": false,
  "instructions": "Be concise.",
  "temperature": 1.0,
  "top_p": 1.0,
  "max_output_tokens": null,
  "tools": [],
  "tool_choice": "auto",
  "parallel_tool_calls": true,
  "truncation": "disabled",
  "presence_penalty": 0,
  "frequency_penalty": 0,
  "top_logprobs": 0,
  "store": false,
  "background": false,
  "service_tier": "default",
  "metadata": null,
  "previous_response_id": null,
  "reasoning": null,
  "max_tool_calls": null,
  "text": { "format": { "type": "text" } }
}
```

**Required fields:** `model`, `input`

**Input formats:** The `input` field accepts three shapes:

```jsonc
// 1. Plain string
"input": "Hello"

// 2. Array of message objects
"input": [
  { "role": "user", "content": "Hello" },
  { "role": "assistant", "content": "Hi there!" },
  { "role": "user", "content": "How are you?" }
]

// 3. Array with structured content parts
"input": [
  {
    "role": "user",
    "content": [
      { "type": "input_text", "text": "What's in this image?" },
      { "type": "input_image", "image_url": "https://example.com/photo.jpg" }
    ]
  }
]
```

### Response body (non-streaming)

All 31 spec-required fields are always present. Nullable fields appear as
`null`, never omitted.

```json
{
  "id": "resp_abc123",
  "object": "response",
  "created_at": 1774585000,
  "status": "completed",
  "model": "gpt-5.4-mini-2026-03-17",
  "output": [
    {
      "type": "message",
      "id": "msg_abc123",
      "status": "completed",
      "role": "assistant",
      "content": [
        {
          "type": "output_text",
          "text": "The capital of France is Paris.",
          "annotations": [],
          "logprobs": []
        }
      ]
    }
  ],
  "usage": {
    "input_tokens": 12,
    "output_tokens": 9,
    "total_tokens": 21,
    "input_tokens_details": { "cached_tokens": 0 },
    "output_tokens_details": { "reasoning_tokens": 0 }
  },
  "completed_at": 1774585001,
  "incomplete_details": null,
  "previous_response_id": null,
  "instructions": null,
  "error": null,
  "reasoning": null,
  "max_output_tokens": null,
  "max_tool_calls": null,
  "safety_identifier": null,
  "prompt_cache_key": null,
  "temperature": 1,
  "top_p": 1,
  "tools": [],
  "tool_choice": "auto",
  "truncation": "disabled",
  "parallel_tool_calls": true,
  "text": { "format": { "type": "text" } },
  "presence_penalty": 0,
  "frequency_penalty": 0,
  "top_logprobs": 0,
  "store": false,
  "background": false,
  "service_tier": "default",
  "metadata": null
}
```

### Response status values

| Status | Meaning |
|--------|---------|
| `completed` | Normal finish |
| `incomplete` | Truncated (e.g., hit `max_output_tokens`); see `incomplete_details` |
| `failed` | Upstream or proxy error; see `error` |
| `in_progress` | Only seen in streaming events |

---

## Tool calling round-trip

### Step 1 — Send a request with tools

```json
{
  "model": "gpt-5.4-mini",
  "input": "What's the weather in Seattle?",
  "tools": [
    {
      "type": "function",
      "name": "get_weather",
      "description": "Get current weather for a city",
      "parameters": {
        "type": "object",
        "properties": {
          "city": { "type": "string" }
        },
        "required": ["city"]
      },
      "strict": null
    }
  ],
  "tool_choice": "auto"
}
```

**`tool_choice` options:**

| Value | Behavior |
|-------|----------|
| `"auto"` | Model decides whether to call tools |
| `"required"` | Model must call at least one tool |
| `"none"` | Model must not call any tools |
| `{ "type": "function", "name": "get_weather" }` | Force a specific function |

### Step 2 — Receive a function_call item

```json
{
  "output": [
    {
      "type": "function_call",
      "id": "fc_abc123",
      "status": "completed",
      "name": "get_weather",
      "call_id": "call_xyz789",
      "arguments": "{\"city\":\"Seattle\"}"
    }
  ]
}
```

### Step 3 — Execute the function and send the result back

```json
{
  "model": "gpt-5.4-mini",
  "input": [
    { "role": "user", "content": "What's the weather in Seattle?" },
    {
      "type": "function_call",
      "name": "get_weather",
      "call_id": "call_xyz789",
      "arguments": "{\"city\":\"Seattle\"}"
    },
    {
      "type": "function_call_output",
      "call_id": "call_xyz789",
      "output": "{\"temp_f\": 58, \"condition\": \"Cloudy\"}"
    }
  ]
}
```

### Step 4 — Receive the final text response

```json
{
  "output": [
    {
      "type": "message",
      "role": "assistant",
      "content": [
        {
          "type": "output_text",
          "text": "The weather in Seattle is 58°F and cloudy.",
          "annotations": [],
          "logprobs": []
        }
      ]
    }
  ]
}
```

---

## Streaming

Set `"stream": true` to receive Server-Sent Events. The response uses
`Content-Type: text/event-stream` with `\n` line endings.

### Event sequence

```
event: response.created
data: { "type": "response.created", "sequence_number": 0, "response": { ... } }

event: response.in_progress
data: { "type": "response.in_progress", "sequence_number": 1, "response": { ... } }

event: response.output_item.added
data: { "type": "response.output_item.added", "sequence_number": 2, "output_index": 0, "item": { ... } }

event: response.content_part.added
data: { "type": "response.content_part.added", "sequence_number": 3, "output_index": 0, "content_index": 0, "part": { ... } }

event: response.output_text.delta
data: { "type": "response.output_text.delta", "sequence_number": 4, "output_index": 0, "content_index": 0, "delta": "The " }

event: response.output_text.delta
data: { "type": "response.output_text.delta", "sequence_number": 5, "output_index": 0, "content_index": 0, "delta": "capital " }

...

event: response.output_text.done
data: { "type": "response.output_text.done", "sequence_number": N, "output_index": 0, "content_index": 0, "text": "The capital of France is Paris." }

event: response.content_part.done
data: { ... }

event: response.output_item.done
data: { ... }

event: response.completed
data: { "type": "response.completed", "sequence_number": N, "response": { ... } }

data: [DONE]
```

### Streaming event types

| Event | When |
|-------|------|
| `response.created` | Response object created |
| `response.in_progress` | Processing started |
| `response.output_item.added` | New output item begins |
| `response.content_part.added` | Content part initialized |
| `response.output_text.delta` | Text token received |
| `response.output_text.done` | Content part text finalized |
| `response.content_part.done` | Content part complete |
| `response.output_item.done` | Output item complete |
| `response.function_call_arguments.delta` | Tool call argument chunk |
| `response.function_call_arguments.done` | Tool call arguments finalized |
| `response.completed` | Successful completion |
| `response.incomplete` | Truncated (length limit) |
| `response.failed` | Error during processing |
| `[DONE]` | Stream terminated |

---

## Errors

Errors return a structured envelope:

```json
{
  "error": {
    "message": "The model 'nonexistent' does not exist.",
    "type": "invalid_request_error",
    "code": "model_not_found",
    "param": "model"
  }
}
```

### Error codes

| Code | Type | Cause |
|------|------|-------|
| `model_not_found` | `invalid_request_error` | Unknown model ID |
| `missing_required_parameter` | `invalid_request_error` | `model` or `input` missing |
| `invalid_input_format` | `invalid_request_error` | `input` is not a string, object, or array |
| `unsupported_model_endpoint` | `invalid_request_error` | Model doesn't support the requested endpoint |
| `auth_error` | `authentication_error` | No Copilot credential loaded |
| `invalid_upstream_response` | `server_error` | Upstream returned unparseable response |
| `stream_error` | `server_error` | Error during stream processing |

Upstream errors (401, 429, 5xx) are mapped into the same envelope with
the appropriate type (`authentication_error`, `rate_limit_error`,
`server_error`).

---

## Output item types

| Type | Fields | Description |
|------|--------|-------------|
| `message` | `role`, `content[]` | Text response from the model |
| `function_call` | `name`, `call_id`, `arguments` | Model wants to call a tool |
| `function_call_output` | `call_id`, `output` | Result of a tool execution (input only) |
| `reasoning` | `summary[]`, `content[]`, `encrypted_content` | Chain-of-thought (when available) |

---

## Two code paths

The proxy routes requests based on model capability:

```
Request → Model supports /responses natively?
  ├─ YES → Passthrough to upstream /responses (with light normalization)
  └─ NO  → Translate to /chat/completions, map response back
```

| Path | Models | Handler |
|------|--------|---------|
| Native | `gpt-5.4-mini`, `gpt-5-mini`, `gpt-5.1`, `gpt-5.2`, etc. | Direct passthrough |
| Translated | `claude-haiku-4.5`, `claude-sonnet-4.5`, `claude-opus-4.6`, etc. | `ChatCompletionsTranslator` |

Both paths produce identical spec-compliant response envelopes.
