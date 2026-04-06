---
title: "llm-agent: Steering and follow-up queues"
status: open
priority: medium
created: 2026-04-06
---

# llm-agent: Steering and follow-up queues

## Summary

Add mid-run steering (interrupt and redirect the agent) and follow-up queues (queue additional prompts to run after the current task completes) to the agent loop.

## Motivation

pi-agent-core's most powerful feature beyond the basic loop is its two-level architecture: an inner loop (single task) and an outer loop (processes a queue of tasks). Steering lets a user say "stop what you're doing and do this instead." Follow-ups let a user say "when you're done, also do this." Without these, agents are fire-and-forget — you can't course-correct a running agent.

## Proposal

### Goals

- Steering: inject a new prompt that replaces the current task mid-run
- Follow-up queue: append prompts to be processed after the current task completes
- The outer loop processes the follow-up queue sequentially
- Steering and follow-ups are delivered via a channel/callback mechanism

### Non-Goals

- Priority ordering of follow-ups
- Concurrent follow-up execution
- Persistent queue across process restarts

## Design

This requires the stateful `Agent` wrapper (see separate plan). The `Agent` class wraps `AgentLoop.RunAsync` with an outer loop that processes a `Channel<string>` of prompts. Steering writes to a separate steering channel that the inner loop checks between turns — if a steering prompt arrives, the inner loop breaks and the outer loop starts a new inner loop with the steering prompt. Follow-ups are appended to the prompt queue. The `AgentLoop.RunAsync` remains stateless; all queue/steering logic lives in the `Agent` wrapper.

## Tasks

- [ ] Design steering channel mechanism (checked between turns)
- [ ] Design follow-up queue (processed by outer loop)
- [ ] Implement steering interrupt in inner loop
- [ ] Implement outer loop in `Agent` wrapper
- [ ] Add `AgentSteered` event type
- [ ] TDD: steering mid-run replaces current task
- [ ] TDD: follow-up runs after current task completes

## Open Questions

- Should steering preserve the current context or start fresh?
- How does steering interact with tool execution in progress?
- Should follow-ups inherit the context from the previous task?
