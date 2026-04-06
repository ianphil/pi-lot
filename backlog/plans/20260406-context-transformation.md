---
title: "llm-agent: Context transformation pipeline"
status: open
priority: medium
created: 2026-04-06
---

# llm-agent: Context transformation pipeline

## Summary

Add a pluggable context transformation step that runs before each LLM call, enabling context pruning, external context injection, and message filtering as conversations grow.

## Motivation

As multi-turn agent conversations grow, the context array becomes unbounded. Without intervention, long conversations hit token limits or degrade response quality. pi-agent-core solves this with a `transformContext` hook that processes the context before conversion to LLM format. This is essential for production agents that run extended sessions.

## Proposal

### Goals

- `IContextTransformer` interface with a `TransformAsync` method that receives the current context items and returns (potentially modified) context items
- Runs before `SerializeInput()` on every turn after the first
- Multiple transformers execute in pipeline order
- Built-in `SlidingWindowTransformer` that keeps the last N items (or last N tokens worth)

### Non-Goals

- Token counting (requires tokenizer — use item count as proxy in V1)
- Automatic summarization of pruned context
- Context persistence/checkpointing

## Design

Add `IContextTransformer` interface. `AgentLoopOptions` gets a `ContextTransformers` list. Before building each request, the loop passes `AgentContext.Items` through the transformer pipeline. Transformers return a new list (they don't mutate the original). The loop uses the transformed list for serialization but keeps the full context for the `AgentEnded` event. Built-in `SlidingWindowTransformer` takes a `maxItems` parameter and always preserves the first item (user prompt) plus the last N items.

## Tasks

- [ ] Define `IContextTransformer` interface
- [ ] Add `ContextTransformers` to `AgentLoopOptions`
- [ ] Integrate transformer pipeline into `AgentLoop` before `BuildRequest`
- [ ] Implement `SlidingWindowTransformer` (keep first + last N items)
- [ ] TDD: transformer prunes old items from serialized input
- [ ] TDD: full context preserved in `AgentEnded` regardless of transformation
- [ ] TDD: multiple transformers execute in pipeline order

## Open Questions

- Should `AgentEnded` carry both full and transformed context?
- Should there be a `ContextTransformed` event for observability?
