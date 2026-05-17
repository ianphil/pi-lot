---
description: 'Keep Markdown documentation concise, current, and linked to the owning source.'
applyTo: '**/*.md'
---

# Documentation

Update docs when changing user-facing behavior, public SDK APIs, HTTP endpoints,
CLI flags, event IDs, install steps, or conformance status.

Keep README focused on overview and quick start. Keep `CONTRIBUTING.md` focused
on contributor workflow and command reference. Put code-generation guidance in
scoped `.github/instructions/*.instructions.md` files.

Prefer concise GitHub-flavored Markdown. Use tables when they make command,
surface, or ownership matrices easier to scan. Do not duplicate long command
blocks across files; link to the canonical command reference instead.
