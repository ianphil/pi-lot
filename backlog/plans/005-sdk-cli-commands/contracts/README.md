# SDK CLI Commands Contracts

Interface definitions for the sdk-ask and sdk-chat CLI subcommands.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [sdk-agents.md](sdk-agents.md) | Agent static method signatures and behavior |

## Contract Principles

- SDK agents are stateless — static methods, no constructor state
- Agents take ILlmSdkClient (already an abstraction) rather than delegates
- Agents take TextWriter for output, enabling test capture
- No tool support in MVP — ToolsEnabled on AskRequest is ignored
