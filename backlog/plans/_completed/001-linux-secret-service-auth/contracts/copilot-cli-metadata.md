# Copilot CLI Metadata Contract

## Purpose

Define the non-secret config fields the Linux credential store may read to choose the preferred Copilot account without turning `~/.copilot/config.json` into a secret source.

## Expected Source

- Path: `~/.copilot/config.json`
- Use: account preference only
- Not allowed: token extraction or plaintext credential fallback

## Consumed Fields

| Field | Type | Meaning |
|-------|------|---------|
| `last_logged_in_user` | `string?` | Preferred GitHub login for the current CLI user |
| `logged_in_users` | `object?` | Set/map of known GitHub logins that can validate the preferred user name |

## Behavioral Contract

| Rule | Description |
|------|-------------|
| Missing config | Treat as no preference |
| Malformed config | Treat as no preference and continue with Secret Service fallback ordering |
| Secret handling | Ignore any fields that appear to store tokens or plaintext secrets |
| Host assumptions | This feature maps logins to `https://github.com:<login>` accounts only |

## Derived Selection Rules

1. If `last_logged_in_user` is present, construct `https://github.com:<login>` and prefer that Secret Service account.
2. If the preferred account is absent, enumerate GitHub.com accounts and choose the first deterministic match.
3. If no GitHub.com accounts exist, return no credential.
