---
description: 'Keep llm-cli focused on proxy-backed command behavior.'
applyTo: 'src/llm-cli/**/*.cs,tests/llm-cli.Tests/**/*.cs'
---

# CLI Boundary

`llm-cli` is a separate deployable reference client. Its `ask` and `chat`
commands talk to the proxy over HTTP through the OpenAI .NET SDK. It should not
reference `llm-svc` projects.

Keep command behavior focused on the user-facing CLI surface: command parsing,
stream/non-stream dispatch, tool execution, process behavior, and endpoint
connectivity. Direct SDK and service/proxy correctness belongs in SDK and service
tests, not in the CLI matrix.

`AskAgent` takes delegate functions for `createAsync` and `createStreamingAsync`
instead of a concrete `ResponsesClient`; preserve that test seam. Tests fake CLI
behavior through function delegates and scripted results, not framework mocks.

New local tools implement `ILocalTool`; dispatch goes through `IToolRegistry`.
