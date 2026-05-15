---
name: issue-slate
description: Batch workflow for llm-svc issue slates. Use this when the user asks to work through a group of GitHub issues by label, priority, milestone, or explicit issue list. It triages the slate, groups issues into small independent PRs, creates a roadmap and todos, then executes each item with a TDD mini-plan.
---

# Issue Slate Skill

Drive a labeled or explicitly supplied GitHub issue slate from triage through reviewable PRs.

Use `gh` for all GitHub operations. Never use MCP. The default base branch is `main`, and the default repository is `ianphil/copilot-llm-svc`.

Always read `CONTRIBUTING.md` before planning or editing. It is the source of truth for build commands, test categories, test locations, branch workflow, and E2E rules.

## When to use

Use this skill when the user asks to:

1. Work through all issues with a label such as `p0`, `p1`, or `llm-sdk`.
2. Turn a set of GitHub issues into an execution plan.
3. Create several PRs from an issue batch.
4. Run a repeatable **plan -> do -> validate -> PR** loop.
5. Generalize, replay, or continue a prior issue-slate workflow.

Do not use this skill for a single already-scoped PR. Handle that PR directly with the normal repo workflow.

## Defaults

- User: `ianphil`.
- Repository: `ianphil/copilot-llm-svc`.
- Base branch: `main`.
- Labels are priority buckets such as `p0`, `p1`, and component labels such as `llm-sdk`.
- Default slate label: ask if unclear; otherwise use the label named by the user.
- Do not stack PRs. Create small, independent PRs directly against `main`.
- Use `gh` for issue and PR operations.
- Never use MCP.
- Run the narrowest existing dotnet tests that prove the changed surface, then the full PR validation shape before pushing when practical.
- Use `scripts/test-matrix.sh` or `scripts/test-matrix.ps1` only for live E2E validation.
- Run Uncle Bob for non-trivial, runtime, architectural, or security-sensitive PRs, and fix critical findings.
- Include `Closes #NNN` or `Fixes #NNN` for every issue intentionally addressed by a PR.
- Do not merge PRs unless the user explicitly asks. Creating reviewable PRs is the default end state.

## Autopilot defaults

Issue-slate work is intended to run without repeated user intervention after the slate is confirmed. Record these defaults in the roadmap and apply them to every PR:

| Prompt area | Default answer |
|---|---|
| Version bump | Follow `CONTRIBUTING.md`; only bump component versions when the change warrants it. |
| Changelog | Update docs/changelog only when the repo convention or user request calls for it. |
| Closing issue | Include all issues covered by the PR group. |
| Uncle Bob | Run automatically for non-trivial/runtime/security/architecture changes; skip for docs/tests/simple UI fixes. |
| E2E matrix | Use `scripts/test-matrix.*` only for real end-to-end CLI/proxy/SDK validation. |
| PR base | Always target `main`; do not create stacked PR bases. |

Do not ask these questions again per PR unless the implementation discovers ambiguity, failures, or a scope change. Merging remains a human gate unless Ian explicitly says to merge approved PRs as they pass.

## Test defaults

Read `CONTRIBUTING.md` first, then choose the narrowest test that proves the changed path:

| Changed area | Default validation |
|---|---|
| `src/llm-sdk/` SDK client, Core models/services, Infrastructure adapters | `dotnet test tests/llm-sdk.Tests/llm-sdk.Tests.csproj --no-restore` |
| `src/llm-svc/` host, HTTP endpoints, service wiring | `dotnet test tests/llm-svc.Tests/llm-svc.Tests.csproj --filter "Category!=Smoke" --no-restore` |
| `src/llm-cli/` commands, CLI agents, local tools | `dotnet test tests/llm-cli.Tests/llm-cli.Tests.csproj --filter "Category!=Smoke" --no-restore` |
| `src/llm-agent/` agent library | `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore` |
| `src/llm-ui/` browser UI behavior | `dotnet test tests/llm-ui.Tests/llm-ui.Tests.csproj --no-restore` |
| Live CLI/proxy/SDK behavior | `scripts/test-matrix.sh` or `scripts/test-matrix.ps1` |

Unit and integration tests belong under the owning project in `tests/`:

| Changed surface | Test project |
|---|---|
| SDK client, Core models/services, Infrastructure adapters | `tests/llm-sdk.Tests/` |
| Host, HTTP endpoints, service wiring | `tests/llm-svc.Tests/` |
| CLI commands, CLI agents, local tools | `tests/llm-cli.Tests/` |
| Agent loop, agent events, context budget | `tests/llm-agent.Tests/` |
| Browser UI behavior | `tests/llm-ui.Tests/` |

Fakes, mocks, stubs, and test servers are fine in unit and integration tests under the owning `tests/*.Tests` project. They are not allowed in E2E test-matrix paths. The `scripts/test-matrix.*` scripts must run real CLI/proxy or SDK paths with real auth and actual LLM calls; do not add fake/faux/mock/stub/test-project/dotnet-test execution to the matrix.

## Phase 1 - Discover and triage the slate

1. Verify the local branch and working tree:

   ```powershell
   git --no-pager status --short --branch
   ```

   If the tree is dirty, stop and ask the user whether to commit, stash, or abort.

2. Sync issue metadata with `gh`:

   ```powershell
   gh issue list --repo ianphil/copilot-llm-svc --state open --label <label> --limit 100 --json number,title,labels,state,url,updatedAt
   gh issue list --repo ianphil/copilot-llm-svc --state all --limit 100 --json number,title,labels,state,url,updatedAt
   ```

3. Check whether all relevant issues have an expected priority/component label such as `p0`, `p1`, or `llm-sdk`.

4. Identify stale, duplicate, already-fixed, invalidated, or superseded issues. Do not close or relabel without user approval.

5. Cluster issues into likely PR groups. Prefer small PRs, but group issues when they share one root cause or one coherent behavior.

6. Identify dependencies and sequencing constraints, but do not turn them into stacked PRs. If two issues cannot be reviewed independently, either group them into one coherent PR or make the dependency explicit in the roadmap while still targeting each PR at `main`.

7. Present a concise slate proposal and ask for confirmation before creating the roadmap when grouping or sequencing is ambiguous.

## Phase 2 - Create the roadmap

Create or update the session roadmap at the current session plan path. The roadmap must include:

1. Goal and scope.
2. Global defaults and merge policy.
3. Independent PR map.
4. One section per PR group with:
   - Branch name.
   - Base branch.
   - Covered issue numbers.
   - Scope.
   - Validation notes.
   - TDD mini-plan placeholder.
5. Execution workflow.
6. Open questions.

Branch naming:

- `fix/<short-desc>-<issue-number>` for fixes.
- `feat/<short-desc>-<issue-number>` for features.
- For grouped issues, include the lead issue number and any essential paired issue numbers.

## Phase 3 - Mirror todos

Create one todo per planned PR group. Add dependencies only when one independent PR logically cannot start until another lands.

Todo IDs should be stable and descriptive, for example:

```text
pr-repo-hygiene-141
pr-genesis-chat-ready-context-92-30
pr-duplicate-mind-session-tools-33
```

Mark each todo `in_progress` before starting it and `done` after its PR is open and clean.

## Phase 4 - Execute each PR group

For each ready todo, run the loop below. Do not start the next PR until the current PR is open, pushed, and recorded in the roadmap.

### 1. Sync and branch

```powershell
git switch main
git fetch origin main --quiet
git pull --ff-only origin main
git switch -c <branch>
```

Do not use Git Town or manual stacked branches for this repo.

### 2. Create PR bases

Open each PR directly against `main`:

```powershell
gh pr create --base main --head <branch>
```

### 3. Inspect the issue and code

Read the GitHub issue body and comments:

```powershell
gh issue view <issue-number> --repo ianphil/copilot-llm-svc --comments
```

Inspect the nearest code, tests, and docs before editing. Follow `CONTRIBUTING.md` and existing repo conventions.

### 4. Write the TDD mini-plan

Before implementation, update the roadmap section for this PR group with:

1. The behavior being proven.
2. The failing automated test, focused smoke, or executable repro to write or observe first.
3. The minimal implementation path.
4. The focused tests, full checks, and runtime smoke that prove completion.

### 5. Red phase

Write the smallest failing test first whenever practical.

If the defect cannot be reproduced in an automated test, capture the closest executable smoke/repro before changing production code. Do not use that exception to skip validation.

### 6. Green phase

Implement surgically until the focused test passes. Keep changes limited to the PR group's scope. Do not fix unrelated pre-existing issues.

### 7. Refactor and validate

Run focused tests during development, then run the appropriate unit, integration, or live E2E validation for changed runtime surfaces.

Before pushing a PR, run the full PR validation shape when practical:

```powershell
dotnet restore copilot-llm.sln
dotnet build copilot-llm.sln --configuration Release --no-restore --disable-build-servers -m:1 --no-incremental
dotnet test copilot-llm.sln --configuration Release --no-build --filter "Category!=Smoke"
```

### 8. Commit

Commit with a conventional title and the required trailer:

```text
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

### 9. Open or update the PR

Open or update a PR directly against `main`. Include issue closure keywords, validation performed, and any intentional scope limits.

### 10. Record result

After the PR is open:

1. Add the PR URL to the roadmap.
2. Record any changed dependency or follow-up.
3. Mark the todo `done`.
4. Move to the next ready todo.

## Phase 5 - Human merge gate

Do not merge by default.

When the user explicitly approves a PR merge:

1. Verify checks are green:

   ```powershell
   gh pr checks <pr-number> --repo ianphil/copilot-llm-svc
   ```

2. Admin squash merge only if the user asks for admin merge:

   ```powershell
   gh pr merge <pr-number> --repo ianphil/copilot-llm-svc --admin --squash --delete-branch
   ```

3. Pull latest `main`:

   ```powershell
   git switch main
   git pull --ff-only origin main
   ```

4. Delete the merged feature branch when appropriate.

## Phase 6 - Closeout

At the end of a slate:

1. Verify the original issue set is closed or intentionally deferred.
2. Verify no open issue unexpectedly remains with the slate label:

   ```powershell
   gh issue list --repo ianphil/copilot-llm-svc --state open --label <label> --limit 100
   ```

3. Confirm all PR URLs are recorded in the roadmap.
4. If appropriate, propose the next slate by adjusting priority/component labels, but do not relabel without user approval.

## Failure modes

- Dirty tree before starting: stop and ask.
- Ambiguous issue grouping: ask before planning.
- Rebase conflict: stop and surface conflicts.
- Failing focused test after implementation attempt: debug; if still failing, stop with evidence.
- Failing lint/test/smoke: stop, show the failure, do not open or merge the PR.
- Security-sensitive finding from Uncle Bob: address or ask before proceeding.
- PR checks fail after opening: fix the branch before asking the user to merge.
- User asks to merge but checks are red: do not merge unless the user explicitly confirms the risk after seeing the failures.

## Notes

- Last proven llm-svc shape: small independent PRs against `main`, with unit/integration tests in the owning `tests/*.Tests` project and live E2E coverage through `scripts/test-matrix.*` only.
- Keep the roadmap lightweight, but keep each PR mini-plan specific. The value is in repeating small, reviewable loops rather than doing one giant implementation pass.
- Prefer transparency over automation magic. The agent owns orchestration; GitHub remains the source of truth for issues and PRs.
