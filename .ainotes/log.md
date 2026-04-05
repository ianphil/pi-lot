# AI Notes — Log

## 2026-04-04
- auth: Current Linux Copilot CLI metadata can store `last_logged_in_user` as a `{ host, login }` object and `logged_in_users` as an array of objects, so the metadata reader must handle more than simple string/map shapes.
- validation: Linux auth smoke checks need both `/health` and `/v1/models` or `llm models`, because healthy auth state alone only proves a token was loaded, not that the upstream credential works.
- dbus: `Tmds.DBus.Protocol` `ConnectAsync()` returns `ValueTask`, not `Task` — needs `.AsTask()` before `.WaitAsync(TimeSpan)`.
- dbus: `ISecretServiceClient` was collapsed from two methods (`SearchItems` + `GetSecret`) to one (`GetCredentialSecret` with selector delegate) to avoid opening two D-Bus connections per credential lookup.
- testing: `CopilotClientTests` mutates the process-global `COPILOT_TOKEN` env var — needs `[Collection("EnvironmentTests")]` to prevent xUnit parallel conflicts.
- deployment: Linux install uses a systemd user service (`systemctl --user`), not a system service — the user session provides `DBUS_SESSION_BUS_ADDRESS` needed for Secret Service credential lookup.
- deployment: Framework-dependent publish on Linux requires `DOTNET_ROOT` in the unit file when dotnet is managed by mise (not at `/usr/share/dotnet`).
- deployment: `dotnet publish` must target the `.csproj`, not the `.sln` — solution-level `--output` copies all projects to the same directory.
- testing: `SecretServiceDbusClient` is intentionally internal to the library, so DI tests should assert the public `ISecretServiceClient` registration rather than depend on the concrete type.
- scripts: `scripts/test-linux-auth.sh` advertises default port `5110`, but the script variable still defaults to `5100`, so smoke runs should pass `--port` explicitly until that mismatch is fixed.
- architecture: `CopilotClient` (Infrastructure) was calling `ChatCompletionsTranslator` (Core/Services) for two pure static transforms — a dependency rule violation. Static helpers that only depend on Core/Models belong in Core/Models, not Core/Services.
- testing: Duplicated test fakes across test projects (identical 73-line classes) should be consolidated into a single shared fake in the library test project, referenced by the host test project.
- release: `main` currently carries aligned `svc-v0.6.1`, `lib-v0.1.0`, and `cli-v0.3.0` tags on the library-extraction baseline commit.
- auth: Request-level refresh has to cover `GET /models` too, because `ChatAsync` resolves model capabilities before routing and can hit the same 401 race as LLM POSTs.
- testing: Injecting `TimeProvider` into `CopilotClient` is enough to test token TTL behavior deterministically without expanding the public SDK surface.
- api-surface: Library v0.2.0 has no `CopilotLlm.Client` or `CopilotLlm.Proxy` namespaces — all types live under `CopilotLlm.Core.*` and `CopilotLlm.Infrastructure`.
- api-surface: `ResponseHttpResult` carries `Body` (string) or `Chunks` (IAsyncEnumerable<string>) — the SDK client layer must parse both shapes to produce typed results.
- api-surface: All 15+ SSE event types are serialized as anonymous JSON objects in `ResponseSseSerializer` — no typed event models exist yet; streaming SDK must define and parse them.
- http: `AddHttpClient()` leaves `HttpClient.Timeout` at the framework default 100 seconds unless the named `CopilotClient` registration overrides it, so the SDK's 120-second default must be applied explicitly.
- serialization: `ChatMessage.Content` deserializes as `object?`, so SDK helpers like `GetMessageText()` should handle both plain `string` and string-valued `JsonElement`.
- transport: `ProxyHttpResult` and `ResponseHttpResult` do not carry response headers, so SDK `RateLimitException.RetryAfter` can only be populated from error JSON until header support exists.
- sdk: Convenience response overloads can build string input with `JsonSerializer.SerializeToElement(..., JsonDefaults.Web)` instead of `JsonDocument.Parse(...).RootElement.Clone()`, avoiding temporary document lifetime handling.
- models: `ModelListService.GetModelsAsync()` already filters out models without proxy-supported endpoints and projects to `OpenAIModelInfo`, so the SDK client can safely expose `response.Data` directly.
- streaming: Chat completion streams still arrive as SSE envelopes (`data: {...}\n\n` plus `[DONE]`), so SDK consumers need envelope parsing before `ChatCompletionChunk` deserialization even when the payload is already chat-shaped.
- serialization: Anonymous payloads that embed a concrete `ResponseMessageItem` lose the polymorphic `type` discriminator unless the value is serialized through the abstract `ResponseItem` base, which matters for SSE parser tests and round-tripping.
