---
title: "llm-sdk: Context overflow detection"
status: open
priority: low
created: 2026-04-06
---

# llm-sdk: Context overflow detection

## Summary

Detect when a conversation's context is approaching or exceeding a model's token limit, enabling proactive handling before the API returns an error.

## Motivation

pi-ai implements context overflow detection via pattern-matching on context size. Without this, long agent conversations fail abruptly when they hit the model's context window. Early detection allows the agent (or consumer) to take corrective action — truncate, summarize, or warn — before hitting a hard API error.

## Proposal

### Goals

- Estimate token count for the current context (heuristic-based, not exact)
- Compare against known model context window sizes
- Emit a warning event or signal when approaching the limit (e.g., 80% threshold)
- Integrate with context transformation pipeline to trigger automatic pruning

### Non-Goals

- Exact tokenizer implementation (use character/word ratio heuristic)
- Automatic context summarization
- Dynamic model-switching based on context size

## Design

Add a `ContextSizeEstimator` that approximates token count from serialized context (e.g., chars/4 as a rough heuristic). A `ModelContextLimits` registry maps model names to known context window sizes. Before each turn, the loop checks estimated tokens against the model's limit. If above a configurable threshold, emit a `ContextOverflowWarning` event. This integrates naturally with the context transformation pipeline — a `SlidingWindowTransformer` could auto-trigger when overflow is detected.

## Tasks

- [ ] Implement `ContextSizeEstimator` with character-based heuristic
- [ ] Create `ModelContextLimits` registry (well-known model → token limit)
- [ ] Add overflow check before each turn in `AgentLoop`
- [ ] Define `ContextOverflowWarning` event type
- [ ] Integrate with context transformer pipeline (optional auto-prune)
- [ ] TDD: overflow detected when context exceeds threshold
- [ ] TDD: no warning when context is within limits
