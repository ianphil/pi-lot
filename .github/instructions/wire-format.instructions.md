---
description: 'Preserve JSON, SSE, and HTTP wire compatibility rules.'
applyTo: 'src/**/*.cs,tests/**/*.cs'
---

# Wire Format Rules

Use `JsonSerializerDefaults.Web` for JSON serialization so names stay camelCase
and spec-compatible.

SSE output must use `\n` line endings, never `\r\n`.

Service/proxy DTO behavior should preserve wire-compatible request and response
shapes. Keep ergonomic in-process SDK abstractions separate from HTTP proxy DTOs
unless shared semantics belong below both surfaces.

When changing response serialization or translation, validate both native
`/responses` behavior and translated `/chat/completions` fallback behavior.
