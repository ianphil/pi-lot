---
description: 'Maintain the llm-ui host, SPA, dependencies, and UI smoke tests.'
applyTo: 'src/llm-ui/**/*,tests/llm-ui.Tests/**/*'
---

# UI Boundary

`llm-ui` is an experimental local SPA for editing Markdown chat context. The
browser talks only to the local ASP.NET Core host; Copilot auth stays server-side
through `LlmSdk`.

Pin direct SPA dependencies in `src/llm-ui/ClientApp/package.json`. After
dependency changes, install/update the lockfile and run `npm outdated` from
`src/llm-ui/ClientApp`.

Run UI smoke tests from the client app:

```powershell
Push-Location src\llm-ui\ClientApp
npm run smoke
Pop-Location
```

Keep browser behavior tests in `tests/llm-ui.Tests` and Playwright SPA smoke
coverage under `src/llm-ui/ClientApp/tests/`.
