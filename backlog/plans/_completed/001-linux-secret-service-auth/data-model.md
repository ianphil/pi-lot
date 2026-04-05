# Data Model: Linux Secret Service Auth

## Entities

### CopilotCredentialRequest

Represents the lookup rules `CopilotClient` applies when it needs a token.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `envVarName` | `string` | Yes | `COPILOT_TOKEN` | Explicit override source |
| `serviceName` | `string` | Yes | `copilot-cli` | Secret Service item service attribute |
| `windowsTargetPrefix` | `string` | Yes | `copilot-cli/https://github.com` | Windows Credential Manager target prefix |
| `accountPrefix` | `string` | Yes | `https://github.com:` | Linux account filter prefix |

**Relationships:**

- Drives `ICopilotCredentialStore` lookup.

**Invariants:**

- `COPILOT_TOKEN` always takes precedence over secure-store lookup.
- Secret-store lookups must never broaden beyond the configured GitHub account prefix without an explicit future feature change.

### CopilotCliLoginMetadata

Represents the non-secret subset of Copilot CLI config used to choose the preferred Linux account.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `lastLoggedInUser` | `string?` | No | `null` | Preferred GitHub login from CLI metadata |
| `loggedInUsers` | `string[]` | No | `[]` | Known Copilot logins from CLI metadata |
| `configPath` | `string` | Yes | `~/.copilot/config.json` | Source file for metadata lookup |

**Relationships:**

- Helps `LinuxSecretServiceCredentialStore` pick a Secret Service account before reading the secret.

**Invariants:**

- This entity contains metadata only and must never carry token bytes.

### SecretServiceCredentialCandidate

Represents a Secret Service item that could hold a Copilot token.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `label` | `string?` | No | `null` | Human-readable item label |
| `service` | `string` | Yes | `copilot-cli` | Secret Service item service attribute |
| `account` | `string` | Yes | n/a | Account attribute such as `https://github.com:ianphil` |
| `isLocked` | `bool` | Yes | `false` | Whether the item is currently locked |
| `secret` | `string?` | No | `null` | Retrieved token text, if readable |

**Relationships:**

- Filtered and ranked by the Linux credential store.

**Invariants:**

- Only `account` values starting with `https://github.com:` are eligible for this feature.
- Locked items are never used.

### ResolvedCopilotCredential

Represents the token selected for runtime use.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `token` | `string` | Yes | n/a | The usable Copilot token |
| `source` | `enum(env, windows, linux-secret-service)` | Yes | n/a | Where the token came from |
| `account` | `string?` | No | `null` | Associated GitHub account, when known |

**Relationships:**

- Loaded into `CopilotClient._token`.

**Invariants:**

- The token must never be logged in full.
- A missing credential is represented as `null`, not as an empty token.

## State Transitions

### Runtime Token Lifecycle

```text
Unauthenticated
   |
   | TryLoadCredential()
   v
Authenticated
   |
   | ValidateTokenAsync() succeeds
   +----------------------------+
   |                            |
   | upstream 401 / expired     |
   v                            |
Reloading ----------------------+
   |
   | credential found
   v
Authenticated
   |
   | credential unavailable
   v
Degraded
```

| State | Description |
|-------|-------------|
| `Unauthenticated` | Service has no token loaded yet |
| `Authenticated` | `CopilotClient` has a token and can proxy requests |
| `Reloading` | Service is re-running credential resolution after expiry/401 |
| `Degraded` | No credential source succeeded; `/health` reports unauthenticated |

## Data Flow

### Linux Credential Resolution

```text
CopilotClient
  -> load metadata from ~/.copilot/config.json
  -> search Secret Service items with service=copilot-cli
  -> filter account startswith https://github.com:
  -> pick preferred account when metadata matches
  -> otherwise choose first deterministic match
  -> read secret bytes
  -> return ResolvedCopilotCredential(source=linux-secret-service)
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| `CopilotCliLoginMetadata` | Missing or malformed config should be treated as no preference | No exception escapes the metadata reader |
| `SecretServiceCredentialCandidate` | Locked or non-GitHub.com items are ignored | Candidate excluded from selection |
| `ResolvedCopilotCredential` | Empty token values are invalid | Return `null` instead of a credential |
| `CopilotCredentialRequest` | Env-var precedence is mandatory | Unit-test failure if store lookup runs before env-var check |
