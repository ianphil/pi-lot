---
title: "llm-sdk: Unified thinking/reasoning support"
status: open
priority: low
created: 2026-04-06
---

# llm-sdk: Unified thinking/reasoning support

## Summary

Provide a unified abstraction for thinking/reasoning levels across models, mapping a simple effort enum to each model's native mechanism.

## Motivation

pi-ai maps unified thinking levels (`minimal` → `xhigh`) to each model's native reasoning mechanism. Currently, llm-sdk passes `ResponseReasoning` through as-is, which works for models that support the Responses API reasoning format. But different providers use different mechanisms (Claude uses `thinking`, OpenAI uses `reasoning`), and consumers shouldn't need to know which. A unified abstraction makes model-switching seamless.

## Proposal

### Goals

- Unified thinking effort levels (e.g., `low`, `medium`, `high`) mapped per model/provider
- Transparent translation in the SDK — consumer sets effort level, SDK maps to provider format
- Thinking content surfaced in stream events (already partially done via `ReasoningDeltaEvent`)

### Non-Goals

- Custom thinking prompts
- Thinking content caching
- Thinking-specific cost tracking (covered by usage tracking plan)

## Design

Add a `ThinkingLevel` enum to `AgentLoopOptions` (or `CreateResponseRequest`). The SDK's translation layer maps this to the appropriate provider-specific format — `reasoning.effort` for OpenAI, `thinking.budget_tokens` for Claude, etc. The `ChatCompletionsTranslator` already handles request translation; this extends that mapping. Stream events for thinking content already exist (`ReasoningDeltaEvent`, `ReasoningSummaryDeltaEvent`).

## Tasks

- [ ] Research provider-specific thinking/reasoning parameter formats
- [ ] Define `ThinkingLevel` enum or similar abstraction
- [ ] Map thinking levels to provider formats in `ChatCompletionsTranslator`
- [ ] Ensure thinking stream events are forwarded through the agent event system
- [ ] TDD: thinking level maps correctly for OpenAI-native models
- [ ] TDD: thinking level maps correctly for translated (Claude) models
