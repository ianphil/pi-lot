# Event Log & Observability Guide

The Copilot LLM Proxy writes structured logs to a custom Windows Event Log
named `LlmProxy`. This guide covers what gets logged and how to
query it.

## Log configuration

| Setting | Value |
|---------|-------|
| Log name | `LlmProxy` |
| Source name | `LlmProxy` |
| Default level | `Information` |
| ASP.NET Core level | `Warning` (reduces framework noise) |

The Event Log source is registered by the install script
(`scripts\install.ps1`). Log levels are configured in `appsettings.json`.

## Event IDs

### Lifecycle (1xxx)

| ID | Name | Level | Meaning |
|----|------|-------|---------|
| 1000 | ServiceStarted | Info | Service or token validation worker started |
| 1001 | ServiceStopping | Info | Service shutting down |

### Authentication (2xxx)

| ID | Name | Level | Meaning |
|----|------|-------|---------|
| 2000 | CredentialLoaded | Info | Copilot token loaded from Credential Manager |
| 2001 | CredentialMissing | Error | No credential found — requests will fail |
| 2002 | TokenValidated | Debug | Background token check passed |
| 2003 | TokenValidationFailed | Warning | Background token check failed (will retry) |
| 2004 | TokenExpired | Warning | Got 401 from upstream; reloading credential |

### API (3xxx)

| ID | Name | Level | Meaning |
|----|------|-------|---------|
| 3000 | ModelsFetched | Info | Model list fetched (includes count) |
| 3001 | RequestProxied | Info | Request routed (includes model and endpoint) |
| 3002 | RequestFailed | Error | Upstream request failed |
| 3003 | SilentTruncationSuspected | Warning | SDK response stopped due to length near the model context window |

### Errors (4xxx)

| ID | Name | Level | Meaning |
|----|------|-------|---------|
| 4000 | UnexpectedError | Error | Unhandled exception |

### HTTP client events (auto-generated)

ASP.NET Core's `HttpClient` logging emits its own events for every
upstream call:

| ID | Meaning |
|----|---------|
| 100 | Sending HTTP request (method, URI) |
| 101 | Received HTTP response (status, duration) |

These include `TraceId`, `SpanId`, `ConnectionId`, and `RequestId` for
correlation.

## Useful queries

### View the last 20 log entries

```powershell
Get-EventLog -LogName LlmProxy -Newest 20 |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Errors only

```powershell
Get-EventLog -LogName LlmProxy -EntryType Error -Newest 20 |
  Format-Table TimeGenerated, Message -Wrap
```

### Errors and warnings

```powershell
Get-EventLog -LogName LlmProxy -EntryType Error,Warning -Newest 20 |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Events in the last hour

```powershell
Get-EventLog -LogName LlmProxy -After (Get-Date).AddHours(-1) |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Events in a time window

```powershell
Get-EventLog -LogName LlmProxy `
  -After  "2026-03-27 00:00" `
  -Before "2026-03-27 01:00" |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Find all events for a specific request (by TraceId)

Each request gets a unique `TraceId`. Find it in any log entry, then
query for all events sharing that trace:

```powershell
Get-EventLog -LogName LlmProxy -Newest 100 |
  Where-Object { $_.Message -match "TraceId: 61ea5fb5321b70369eb5f0c10fe5cec4" } |
  Format-Table TimeGenerated, Message -Wrap
```

### Count requests by searching for routing events

```powershell
Get-EventLog -LogName LlmProxy -Newest 1000 |
  Where-Object { $_.Message -match "Routing" } |
  Measure-Object
```

### Find requests to a specific model

```powershell
Get-EventLog -LogName LlmProxy -Newest 500 |
  Where-Object { $_.Message -match "Routing claude-haiku" } |
  Format-Table TimeGenerated, Message -Wrap
```

### Find slow upstream calls (>2 seconds)

```powershell
Get-EventLog -LogName LlmProxy -Newest 500 |
  Where-Object {
    $_.Message -match "after (\d+(?:\.\d+)?)ms" -and
    [double]$Matches[1] -gt 2000
  } |
  Format-Table TimeGenerated, Message -Wrap
```

### Startup and shutdown events

```powershell
Get-EventLog -LogName LlmProxy -Newest 100 |
  Where-Object { $_.Message -match "start|stop|shutdown" } |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Authentication issues

```powershell
Get-EventLog -LogName LlmProxy -Newest 100 |
  Where-Object { $_.Message -match "credential|token|401|auth" } |
  Format-Table TimeGenerated, EntryType, Message -Wrap
```

### Watch live (poll every 5 seconds)

```powershell
$last = (Get-Date)
while ($true) {
  $entries = Get-EventLog -LogName LlmProxy -After $last -ErrorAction SilentlyContinue
  if ($entries) {
    $entries | Format-Table TimeGenerated, EntryType, Message -Wrap
    $last = ($entries | Select-Object -First 1).TimeGenerated
  }
  Start-Sleep -Seconds 5
}
```

### Export to CSV for analysis

```powershell
Get-EventLog -LogName LlmProxy -Newest 1000 |
  Select-Object TimeGenerated, EntryType, EventID, Message |
  Export-Csv -Path events.csv -NoTypeInformation
```

### Clear the log (requires admin)

```powershell
Clear-EventLog -LogName LlmProxy
```

## What each component logs

### CopilotClient (authentication & routing)

- Credential loaded / missing on startup
- Token expiration detected (401 response, auto-reload)
- Model list fetched with count
- Request routing decision: which model → which endpoint
- Silent truncation suspicion when a length-limited SDK response is near the model context window

### Worker (background health)

- Token validation worker start
- Periodic validation pass/fail (every 5 minutes)

### HttpClient (automatic per-request)

- Outbound HTTP method and URI
- Response status code and duration in milliseconds
- Full distributed tracing context (TraceId, SpanId, ConnectionId, RequestId)

### Program (startup)

- Service starting confirmation
- Credential load failure warning at boot

## Structured properties

Log entries include structured properties that can be matched with
`-match` in PowerShell:

| Property | Example | Where |
|----------|---------|-------|
| `TraceId` | `61ea5fb5...` | All per-request events |
| `SpanId` | `cc483090...` | All per-request events |
| `ConnectionId` | `0HNKBMMF7OQJU` | Per-connection |
| `RequestId` | `0HNKBMMF7OQJU:00000001` | Per-request |
| `RequestPath` | `/v1/responses` | Inbound request path |
| `HttpMethod` | `POST` | Outbound HTTP method |
| `Uri` | `https://api.enterprise.githubcopilot.com/responses` | Upstream URL |
| `{Model}` | `gpt-5.4-mini` | In routing log entries |
| `{Endpoint}` | `/responses` | In routing log entries |
| `{Count}` | `12` | In model fetch entries |
| `{Prefix}` | `ghu_` | First 4 chars of loaded token |
