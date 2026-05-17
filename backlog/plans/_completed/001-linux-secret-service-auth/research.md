# Linux Secret Service Auth Conformance Research

**Date**: 2026-04-04
**Spec Version Reviewed**: [Secret Service 0.2 DRAFT (2026-02-19)](https://specifications.freedesktop.org/secret-service-spec/latest-single/)
**Plan Version**: plan.md

## Summary

The feature should integrate directly with the Linux desktop secret-store surface that the Copilot CLI already uses: Secret Service over D-Bus. The external conformance target is not the OpenAI proxy API; it is the Secret Service lookup model plus the observed Copilot CLI credential-shape and account-selection behavior documented in issue #1 research.

## Conformance Analysis

### 1. Secret Retrieval Surface

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Secret storage integration | Use Secret Service over D-Bus, not `libsecret` P/Invoke | Secret Service defines items, lookup attributes, and `GetSecret()` / `GetSecrets()` access over D-Bus | CONFORMANT |

**Recommendation**: Keep the integration at the Secret Service API level and avoid native `libsecret` interop.

### 2. Attribute-Based Lookup

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Item discovery | Filter by `service = copilot-cli` and GitHub.com account attributes | The spec recommends lookup by item attributes rather than persisting object paths | CONFORMANT |

**Recommendation**: Model Linux lookup around attribute filtering first, then fetch the secret only for the selected item.

### 3. Locked Collections and User Prompts

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Locked keyrings | Treat locked/unavailable items as a null result and degrade gracefully | The spec allows locked items and prompt-driven unlock flows | UPDATE NEEDED |

**Recommendation**: Do not trigger prompts from `llm-svc`; treat locked items as unavailable because the service is expected to start non-interactively.

### 4. Multi-Account Selection

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| User preference | Prefer Copilot CLI `last_logged_in_user` metadata, then fall back to the first GitHub.com match in deterministic order | Secret Service specifies attribute lookup but not application-specific account preference rules | CONFORMANT |

**Recommendation**: Keep account preference logic outside Secret Service itself by using non-secret CLI metadata only to choose among matching items.

### 5. Headless and Missing Session Bus

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| No desktop session | Return null when D-Bus session or Secret Service is unavailable | The spec assumes a user login session but does not guarantee one exists in server/container environments | CONFORMANT |

**Recommendation**: Treat connection failures as an expected absence-of-feature path, not as a service startup error.

## Copilot CLI Behavior Research

| Finding | Source | Impact on Plan |
|---------|--------|----------------|
| Linux auth uses the system keychain via `keytar` | Issue #1 comment research | Confirms secure-store integration is the right source of truth |
| Secret Service item shape uses `service = copilot-cli` | Issue #1 comment research | Drives the Linux lookup filter |
| Account selection prefers `${host}:${login}` and can fall back when metadata is missing | Issue #1 comment research | Justifies reading non-secret config metadata for `last_logged_in_user` |
| `~/.copilot/config.json` stores metadata, not the primary usable secret | Issue #1 comment research | Keeps config parsing limited to account preference only |

## New Features in Spec (Not in Plan)

- Prompt-driven unlocking of collections or items is intentionally not implemented for this feature because `llm-svc` is a background service/process.
- Secret creation and item mutation are out of scope; the service only reads credentials.

## Recommendations

### Critical Updates

1. Implement the Linux adapter directly on `Tmds.DBus.Protocol` rather than adding a higher-level wrapper package.
2. Keep the Linux metadata reader explicitly limited to non-secret fields so the design does not drift into config-based secret parsing.
3. Make Linux fallback ordering deterministic when `last_logged_in_user` is absent so multiple credentials do not produce nondeterministic startup behavior.

### Minor Updates

1. Preserve existing auth log events and add source-specific message text instead of creating a parallel event taxonomy.
2. Keep `CredentialManager.cs` as the Windows native helper and wrap it rather than renaming platform files unnecessarily.

### Future Enhancements

1. Evaluate macOS keychain support with the same injected credential-store pattern.
2. Consider GitHub Enterprise host selection if Copilot CLI metadata expands to multiple hosts.

## Sources

- [Secret Service API 0.2 DRAFT](https://specifications.freedesktop.org/secret-service-spec/latest-single/)
- [Issue #1 comment: design direction and abstraction proposal](https://github.com/ianphil/pi-lot/issues/1#issuecomment-4187219472)
- [Issue #1 comment: Linux Secret Service spike with D-Bus package evaluation](https://github.com/ianphil/pi-lot/issues/1#issuecomment-4187229758)
- [Issue #1 comment: Copilot CLI auth storage behavior on Linux](https://github.com/ianphil/pi-lot/issues/1#issuecomment-4187238747)

## Conclusion

The plan is aligned with both the freedesktop Secret Service model and the observed Copilot CLI Linux storage behavior. The main design constraints are operational rather than architectural: do not prompt, do not parse plaintext secrets from config, and make multi-account selection deterministic.
