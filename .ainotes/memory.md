# AI Notes — llm-svc

## Rules

- **OutputType must remain `WinExe`** in `src/llm-svc/llm-svc.csproj`. It suppresses the console window when running as a Windows Scheduled Task. Linux ignores the PE subsystem flag entirely, so it works correctly on both platforms. Do not change to `Exe`.
