---
description: 'Maintain the OpenResponses conformance backlog without deleting tracked requirements.'
applyTo: 'backlog/**/*'
---

# Conformance Backlog

`backlog/002-Responses-conformance.json` tracks OpenResponses API spec
conformance.

Each requirement uses:

- `status`: `implemented`, `partial`, `not_implemented`, or `out_of_scope`
- `priority`: `P0`, `P1`, or `P2`
- `complexity`: Fibonacci scale from `1` through `21`

When implementing a conformance item, update its `status` and add notes
describing what changed. Do not remove items. Mark upstream-only concerns as
`out_of_scope` with an explanation.
