# Linux Secret Service Auth Contracts

Interface definitions and integration contracts for Linux Secret Service auth.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [credential-store.md](credential-store.md) | Defines the Infrastructure credential-store abstraction and source precedence |
| [copilot-cli-metadata.md](copilot-cli-metadata.md) | Defines the non-secret Copilot CLI metadata consumed for Linux account selection |

## Contract Principles

- Secrets come from secure stores or `COPILOT_TOKEN`, never from plaintext config parsing.
- Platform-specific credential access remains an Infrastructure concern.
- Linux account selection should be deterministic and explainable from observable metadata.
