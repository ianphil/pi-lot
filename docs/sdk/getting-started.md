# Getting Started

## Install

From GitHub Packages:

```bash
dotnet add package LlmSdk --source https://nuget.pkg.github.com/ianphil/index.json
```

Inside this repo, reference `src/llm-sdk/llm-sdk.csproj` directly.

## Authenticate

The SDK resolves Copilot credentials in this order:

1. `COPILOT_TOKEN` on every platform.
2. Windows Credential Manager entries created by Copilot CLI.
3. Linux Secret Service entries created by Copilot CLI.

On macOS, containers, and headless hosts, set `COPILOT_TOKEN`.

## Register services

```csharp
using LlmSdk;
using LlmSdk.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddLlmSdk(options =>
{
    options.DefaultModel = "gpt-5.4-mini";
    options.HttpTimeout = TimeSpan.FromSeconds(60);
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ILlmSdkClient>();
```

## Make a portable context call

```csharp
using LlmSdk.Core.Models;

var context = new Context
{
    System = "Be concise.",
    Messages =
    [
        new UserMessage([new TextContent("What is 2+2?")]),
    ],
};

var message = await client.CompleteAsync(context, new CompletionOptions
{
    Model = "gpt-5.4-mini",
});

Console.WriteLine(string.Concat(message.Content.OfType<TextContent>().Select(t => t.Text)));
```

Use `CompleteAsync` and `StreamAsync` for portable application code. Use raw
Responses or Chat Completions methods when you need their exact wire shapes.
