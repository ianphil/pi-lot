---
title: "llm-sdk: Cost and usage tracking"
status: open
priority: low
created: 2026-04-06
---

# llm-sdk: Cost and usage tracking

## Summary

Surface token usage breakdowns (input, output, cache read/write) and per-request cost estimates from LLM responses through the SDK.

## Motivation

pi-ai returns token breakdown and cost calculation with every response. When running agents with tool loops, costs compound across turns — without tracking, users have no visibility into spend. The Responses API already returns `usage` in the response object; we just need to surface it cleanly and optionally compute cost.

## Proposal

### Goals

- Parse and expose `ResponseUsage` from completed responses (already partially modeled)
- Aggregate usage across multi-turn agent loops (total tokens for the full conversation)
- Optional cost estimation given a price-per-token table

### Non-Goals

- Real-time cost streaming during a response
- Provider-specific pricing databases (consumer provides the table)
- Budget enforcement / hard limits

## Design

`Response.Usage` already exists in the SDK models. The agent loop should aggregate usage across turns and expose it in `AgentEnded`. A simple `UsageSummary` record tracks total input/output/reasoning tokens across all turns. Cost estimation is a separate utility that takes a `UsageSummary` and a price table and returns a dollar amount. This keeps the core loop free of pricing concerns.

## Tasks

- [ ] Verify `ResponseUsage` is fully parsed from API responses
- [ ] Add `UsageSummary` record to aggregate usage across turns
- [ ] Accumulate usage in `AgentLoop` and expose in `AgentEnded`
- [ ] Create `CostEstimator` utility (price table → dollar amount)
- [ ] TDD: usage accumulates correctly across multi-turn loops
- [ ] TDD: cost estimator calculates correctly from usage + price table
