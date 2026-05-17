---
description: 'Keep llm-agent behavior owned by the agent library and its tests.'
applyTo: 'src/llm-agent/**/*.cs,tests/llm-agent.Tests/**/*.cs,tests/llm-agent.Int/**/*.cs'
---

# Agent Boundary

`llm-agent` owns the agent loop, agent events, tool orchestration, context
updates, context budget behavior, and option forwarding over `ILlmSdkClient`.

Treat `LlmAgent` as a public package surface. Add API deliberately: prefer
agent-run concepts over broad SDK passthrough, and use precise names that match
current semantics until a broader portable abstraction is explicitly designed.
For example, raw Responses-native cache forwarding should use
`PromptCacheKey`; do not rename it to a portable session concept unless the
story owns those semantics.

Keep behavioral capabilities such as abort modes, thinking controls, cache
retention policy, diagnostics, and context migration in explicit API-design
stories. Do not add them incidentally while forwarding unrelated SDK request
options.

Document callback and hook timing. Agent runs may issue multiple SDK requests,
so options such as payload/response hooks need clear per-turn versus per-run
semantics in tests and user docs.

Keep pure event-shape and edge-case coverage in `llm-agent.Tests`. Use
`llm-agent.Int` when the test should prove the public agent API remains a
working reference consumer of `ILlmSdkClient`.

The context budget guard estimates prompt tokens with `Microsoft.ML.Tokenizers`
and treats upstream `token_limits` metadata as the source of model budgets. Keep
budget behavior deterministic in unit tests and small in live smoke tests.
