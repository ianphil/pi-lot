---
description: 'Apply repository C# style conventions.'
applyTo: '**/*.cs'
---

# C# Style

- Target .NET 10 and C# latest with nullable enabled.
- Prefer `record` types for simple DTOs.
- Prefer `GeneratedRegex` over `new Regex()`.
- Prefer `is null` and `is not null` over `== null` and `!= null`.
- Prefer `nameof` over string literals for member references.
- Add comments only when the code needs clarification; do not explain obvious
  mechanics.
- Keep type safety. Avoid unnecessary casts, especially `as any`-style
  equivalents or broad object conversions.
