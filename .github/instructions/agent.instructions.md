---
description: 'Keep llm-agent behavior owned by the agent library and its tests.'
applyTo: 'src/llm-agent/**/*.cs,tests/llm-agent.Tests/**/*.cs,tests/llm-agent.Int/**/*.cs'
---

# Agent Boundary

`llm-agent` owns the agent loop, agent events, tool orchestration, context
updates, context budget behavior, and option forwarding over `ILlmSdkClient`.

Keep pure event-shape and edge-case coverage in `llm-agent.Tests`. Use
`llm-agent.Int` when the test should prove the public agent API remains a
working reference consumer of `ILlmSdkClient`.

The context budget guard estimates prompt tokens with `Microsoft.ML.Tokenizers`
and treats upstream `token_limits` metadata as the source of model budgets. Keep
budget behavior deterministic in unit tests and small in live smoke tests.
