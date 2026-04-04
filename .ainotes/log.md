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
