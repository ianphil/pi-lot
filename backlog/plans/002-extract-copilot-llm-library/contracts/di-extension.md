# Contract: AddCopilotLlm() DI Extension

## Interface

```csharp
namespace CopilotLlm;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCopilotLlm(this IServiceCollection services)
}
```

## Behavior

Registers all CopilotLlm services into the provided service collection:

### Always Registered
| Service Type | Implementation | Lifetime |
|-------------|----------------|----------|
| CopilotClient | CopilotClient | Singleton |
| IAuthProvider | → CopilotClient | Singleton (alias) |
| IModelProvider | → CopilotClient | Singleton (alias) |
| ChatCompletionsTranslator | ChatCompletionsTranslator | Singleton |
| ChatCompletionsStreamTranslator | ChatCompletionsStreamTranslator | Singleton |
| ResponsesStreamToChatTranslator | ResponsesStreamToChatTranslator | Singleton |
| ModelListService | ModelListService | Singleton |
| IResponsesService | ResponsesService | Singleton |
| IChatCompletionsService | ChatCompletionsService | Singleton |
| IHttpClientFactory | (via AddHttpClient) | — |

### Platform-Conditional
| Condition | Service Type | Implementation |
|-----------|-------------|----------------|
| Windows | ICopilotCredentialStore | WindowsCredentialStore |
| Linux | ICopilotCredentialStore | LinuxSecretServiceCredentialStore |
| Linux | CopilotCliConfigMetadataReader | CopilotCliConfigMetadataReader |
| Linux | ISecretServiceClient | SecretServiceDbusClient |
| Other | ICopilotCredentialStore | NoOpCopilotCredentialStore |

### NOT Registered (Host Responsibility)
- Worker / IHostedService (token refresh scheduling)
- Windows Service hosting (AddWindowsService)
- Event Log logging (AddEventLog)
- Credential loading at startup (TryLoadCredential call)

## Return Value

Returns the `IServiceCollection` for chaining.

## Dependencies

The library package requires:
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Http`
- `Tmds.DBus.Protocol` (Linux credential store)
