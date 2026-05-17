# Release Channels

This repo currently releases only the reusable packages:

| Package | Project | Channel | Tag format | Distribution |
|---|---|---|---|---|
| `LlmSdk` | `src/llm-sdk` | Stable | `sdk-vX.Y.Z` | GitHub Packages |
| `LlmAgent` | `src/llm-agent` | Stable | `agent-vX.Y.Z` | GitHub Packages |

`llm-svc`, `llm-cli`, and `llm-ui` are not released through this process yet.
Treat them as local/reference projects until a separate release decision is
made.

## Mental model

- Releases are manual-only. Nothing publishes automatically from a feature PR.
- Feature PRs do not bump `.csproj` versions.
- A project's `.csproj` version represents the last shipped stable version.
- The release workflow computes the release version at dispatch time and applies
  it in the runner workspace for packing.
- Stable tags are package-specific: `sdk-vX.Y.Z` and `agent-vX.Y.Z`.
- Preview channels can be added later with package-specific prerelease tags such
  as `sdk-vX.Y.Z-preview.N` and `agent-vX.Y.Z-preview.N`.

## Stable package release flow

1. Confirm the package's unreleased changes are ready to ship.
2. Run the package-specific release workflow manually.
3. The workflow validates tests for the package and its dependencies.
4. The workflow computes or accepts the target SemVer version.
5. The workflow packs with the computed version in the runner workspace.
6. The workflow publishes the package and pushes the stable tag.
7. After a successful release, open a post-release PR that updates the package
   `.csproj` version to the shipped stable version and records release notes.

## Package-specific notes

### LlmSdk

`LlmSdk` is the lower-level SDK package. Its release workflow should run SDK unit
tests and the CI-safe SDK integration tests before packing.

### LlmAgent

`LlmAgent` is a public package built on `LlmSdk`. Its release workflow should run
agent unit tests and CI-safe agent integration tests. When releasing `LlmAgent`,
the workflow must ensure the package references the intended `LlmSdk` version,
either by packing against an already released SDK package or by using an explicit
release input that identifies the SDK dependency version.

## Deferred release surfaces

Do not add release workflows for these projects until their product shape is
settled:

| Project | Current status |
|---|---|
| `src/llm-svc` | Local OpenAI-compatible proxy/reference host |
| `src/llm-cli` | Reference/dev terminal client |
| `src/llm-ui` | Experimental editable-context UI |
