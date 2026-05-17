# Simple Agent Example

This console app shows the smallest useful `LlmAgent` setup: register `LlmSdk`,
provide one schema-backed tool, run `AgentLoop.RunAsync`, and print streamed
events.

## Run

From the repository root:

```bash
dotnet run --project examples/simple-agent -- "Use the get_current_time tool with timezone set to utc, then tell me the time."
```

The example uses `gpt-5.4-mini` and real Copilot credentials. Set
`COPILOT_TOKEN` or sign in with Copilot CLI before running.

## What it demonstrates

- `services.AddLlmSdk(...)` plus `ILlmSdkClient`
- `AgentLoopOptions` with a schema-backed `IAgentTool`
- streamed assistant text via portable `MessageDelta` / `TextDelta` events
- tool lifecycle events via `ToolExecutionStarted` and `ToolExecutionEnded`
