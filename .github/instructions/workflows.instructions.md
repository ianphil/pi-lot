---
description: 'Keep GitHub Actions workflows minimal, least-privilege, and aligned with repo commands.'
applyTo: '.github/workflows/*.yml,.github/workflows/*.yaml'
---

# GitHub Actions Workflows

Use least-privilege `permissions` blocks. Prefer pinned major versions for
official actions already used in the repo, such as `actions/checkout@v4`,
`actions/setup-dotnet@v4`, and `actions/upload-artifact@v4`.

Keep workflow test commands aligned with `CONTRIBUTING.md`. PR validation should
exclude `Smoke` and `UpstreamCapture` tests unless the workflow explicitly has
live credentials and network assumptions.

When changing solution membership, keep the PR validation solution coverage
check in sync so every `src` and `tests` project remains in `pi-lot.sln`.

Publishing workflows should verify tag/version alignment before pushing
packages.
