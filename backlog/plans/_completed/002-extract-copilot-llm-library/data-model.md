# Data Model: Extract CopilotLlm Library

## Entities

This is a structural refactoring — no new data models are introduced. All existing models move from `LlmSvc.Core.Models` to `CopilotLlm.Core.Models` unchanged.

### Existing Model Groups (Moving)

| Group | File | Types | Description |
|-------|------|-------|-------------|
| Responses API | ResponsesApiModels.cs | 24 types | CreateResponseRequest, Response, ResponseItem hierarchy, ResponseUsage |
| Chat Completions | ChatCompletionModels.cs | 14 types | ChatCompletionRequest/Response/Chunk, ChatMessage, tools |
| OpenAI Models | OpenAIModels.cs | 4 types | OpenAIModelListResponse, OpenAIModelInfo, OpenAIError |
| Copilot API | CopilotApiModels.cs | 4 types | CopilotModelsResponse, CopilotModelInfo, capabilities/limits |
| Deserialization | ResponsesDeserializationModels.cs | 4 types | Internal upstream response parsing |
| Errors | ErrorTypes.cs | 2 types | ErrorTypes, ErrorCodes constants |
| Helpers | JsonElementHelpers.cs | 1 type | JsonElement utility functions |

### New Type: ServiceCollectionExtensions

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| (static class) | — | — | Extension methods on IServiceCollection |

**Methods:**
- `AddCopilotLlm(this IServiceCollection services)` → `IServiceCollection`

**Invariants:**
- Registers CopilotClient as singleton implementing both IAuthProvider and IModelProvider
- Platform-conditional credential store selection
- All translators and services registered as singletons
- Does NOT register Worker or any IHostedService

## State Transitions

No state changes. The library is stateless — CopilotClient holds runtime state (token, models cache) but its lifecycle is unchanged.

## Data Flow

### DI Registration Flow
```
Host calls AddCopilotLlm()
  │
  ├─ Register CopilotCliConfigMetadataReader (Linux only)
  ├─ Register ISecretServiceClient → SecretServiceDbusClient (Linux only)
  ├─ Register ICopilotCredentialStore → platform-conditional factory
  ├─ Register CopilotClient (singleton)
  ├─ Alias IAuthProvider → CopilotClient
  ├─ Alias IModelProvider → CopilotClient
  ├─ Register ChatCompletionsTranslator
  ├─ Register ChatCompletionsStreamTranslator
  ├─ Register ResponsesStreamToChatTranslator
  ├─ Register ModelListService
  ├─ Register IResponsesService → ResponsesService
  └─ Register IChatCompletionsService → ChatCompletionsService
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| CopilotLlm.csproj | Must target net10.0 | Build error |
| CopilotLlm.csproj | Must NOT reference Microsoft.Extensions.Hosting | Unnecessary hosting dep |
| ServiceCollectionExtensions | Must NOT register Worker | Worker is host concern |
| Namespace | All files must use CopilotLlm.* namespace | Build error on old namespace |
