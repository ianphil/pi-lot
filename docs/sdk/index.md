# LlmSdk Developer Docs

`LlmSdk` is the Copilot-backed .NET library in this repo. It provides a
portable context API plus raw Responses and Chat Completions access for callers
that need OpenAI-shaped request and response types.

Use these docs with the XML API docs emitted by `src/llm-sdk/llm-sdk.csproj`.
The Markdown guides explain the intended usage patterns; the XML comments stay
next to the public types and describe exact API contracts.

## Start here

| Guide | Use it for |
|---|---|
| [Getting started](getting-started.md) | Installation, auth, DI registration, first calls |
| [Architecture](architecture.md) | SDK boundaries, ports, adapters, and consumer ownership |
| [Context API](context-api.md) | Portable messages, content blocks, options, usage |
| [Streaming](streaming.md) | Portable streaming events and raw stream events |
| [Tools](tools.md) | Tool definitions, validation, and tool-result turns |
| [Images](images.md) | Vision image inputs and current image-generation limits |
| [Thinking](thinking.md) | Reasoning effort, clamping, and redacted thinking replay |
| [Diagnostics](diagnostics.md) | Structured assistant diagnostics and reserved codes |
| [Errors and retries](errors-retries.md) | Typed exceptions, retry knobs, and auth failures |
| [Raw APIs](raw-apis.md) | Direct Responses and Chat Completions DTOs |
| [Testing](testing.md) | Unit, fake integration, and live smoke coverage |

For a shorter user guide, see [`../sdk-guide.md`](../sdk-guide.md).
