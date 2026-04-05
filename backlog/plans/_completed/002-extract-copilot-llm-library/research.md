# Extract CopilotLlm Library — Research

**Date**: 2026-04-04
**Spec Version Reviewed**: NuGet packaging (.NET SDK), GitHub Packages NuGet
**Plan Version**: plan.md

## Summary

This is an internal refactoring — no external API spec to conform to. Research focuses on .NET class library packaging conventions and GitHub Packages NuGet publishing requirements.

## Conformance Analysis

### 1. NuGet Package Metadata

| Aspect | Plan | .NET SDK Spec | Status |
|--------|------|---------------|--------|
| PackageId | CopilotLlm | Required, unique within feed | CONFORMANT |
| Version | SemVer from csproj | Must be valid SemVer | CONFORMANT |
| TargetFramework | net10.0 | Any valid TFM | CONFORMANT |
| SDK | Microsoft.NET.Sdk | Class library SDK (not Web) | CONFORMANT |
| PackageDescription | Required | Must be non-empty | CONFORMANT |
| RepositoryUrl | GitHub repo URL | Recommended for source linking | CONFORMANT |

### 2. GitHub Packages NuGet Publishing

| Aspect | Plan | GitHub Docs | Status |
|--------|------|-------------|--------|
| Source URL | `https://nuget.pkg.github.com/OWNER/index.json` | Required format | CONFORMANT |
| Authentication | GITHUB_TOKEN or PAT | packages:write scope | CONFORMANT |
| Package naming | Must match or be scoped to repo owner | GitHub enforces owner prefix | UPDATE NEEDED |

**Recommendation**: Verify the PackageId aligns with GitHub's owner-scoping rules. May need `OWNER.CopilotLlm` format.

### 3. Class Library DI Extension Pattern

| Aspect | Plan | .NET Convention | Status |
|--------|------|-----------------|--------|
| Extension method on IServiceCollection | AddCopilotLlm() | Standard pattern (AddXxx) | CONFORMANT |
| Return type | IServiceCollection | Enables chaining | CONFORMANT |
| Package dependency | M.E.DependencyInjection.Abstractions | Lightweight, no hosting dep | CONFORMANT |
| Logging | M.E.Logging.Abstractions | Standard for libraries | CONFORMANT |

## Recommendations

### Critical Updates
1. Confirm GitHub Packages PackageId scoping rules for the repo owner

### Minor Updates
1. Consider adding `PackageLicenseExpression` and `PackageReadmeFile` to csproj for good package hygiene
2. Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for IntelliSense support

### Future Enhancements
1. Source Link integration for package debugging
2. CI workflow for automated publish on version tag

## Conclusion

No external API conformance needed. The packaging approach aligns with .NET and GitHub Packages conventions. The only action item is confirming PackageId scoping for the GitHub NuGet feed.
