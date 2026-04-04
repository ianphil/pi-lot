# Contract: NuGet Package Metadata

## Required csproj Properties

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>CopilotLlm</RootNamespace>

    <!-- Package metadata -->
    <PackageId>CopilotLlm</PackageId>
    <Version>0.1.0</Version>
    <Authors>cip</Authors>
    <Description>A .NET library for accessing GitHub Copilot's LLM API. Provides OpenAI-compatible Responses API and Chat Completions translation, credential resolution, and model discovery.</Description>
    <RepositoryUrl>https://github.com/OWNER/copilot-llm-svc</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <IsPackable>true</IsPackable>

    <!-- Suppress Windows-specific API warnings (credential stores) -->
    <NoWarn>CA1416</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Tmds.DBus.Protocol" Version="0.91.1" />
  </ItemGroup>
</Project>
```

## Publishing

```bash
# Pack
dotnet pack CopilotLlm/CopilotLlm.csproj -c Release

# Push to GitHub Packages
dotnet nuget push CopilotLlm/bin/Release/CopilotLlm.0.1.0.nupkg \
  --source "https://nuget.pkg.github.com/OWNER/index.json" \
  --api-key $GITHUB_TOKEN
```

## Consumer Configuration

Consumers add to their `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/OWNER/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="USERNAME" />
      <add key="ClearTextPassword" value="PAT_WITH_PACKAGES_READ" />
    </github>
  </packageSourceCredentials>
</configuration>
```

## Versioning

- Library starts at 0.1.0
- Independent from llm-svc version (currently 0.6.0)
- Follows SemVer 2.0
- Git tags for library: `lib-v{version}` (distinct from service `v{version}` tags)
