# Endpoint Translation Test Matrix

This matrix tracks coverage for the supported proxy surfaces and their upstream routing or translation behavior.

## Current coverage

| Surface | Upstream model capability | Scenario | Covered | Test |
| --- | --- | --- | --- | --- |
| `/responses` | `/responses` only | Plain text response | Yes | `ResponsesEndpointTests.PostResponses_NativeResponsesModel_ReturnsCanonicalResponseBody` |
| `/responses` | `/responses` only | Streaming response | Yes | `ResponsesEndpointTests.PostResponses_NativeResponsesModel_WithStreaming_ReturnsEventStreamBody` |
| `/responses` | `/chat/completions` only | Plain text translation | Yes | `ResponsesEndpointTests.PostResponses_ChatOnlyModel_TranslatesPlainTextResponse` |
| `/responses` | `/chat/completions` only | Streaming translation | Yes | `ResponsesEndpointTests.PostResponses_ChatOnlyModel_WithStreaming_TranslatesPlainTextStream` |
| `/responses` | `/chat/completions` only | Tool definition forwarding | Yes | `ResponsesEndpointTests.PostResponses_ChatOnlyModel_ForwardsToolDefinitions` |
| `/responses` | `/chat/completions` only | Tool round-trip | Yes | `ResponsesEndpointTests.PostResponses_ChatOnlyModel_WithToolRoundTrip_TranslatesFunctionCallConversation` |
| `/responses` | `/chat/completions` only | Streaming tool round-trip | Yes | `ResponsesEndpointTests.PostResponses_ChatOnlyModel_WithToolRoundTripStreaming_TranslatesFunctionCallConversation` |
| `/responses` | Both `/responses` and `/chat/completions` | Prefer native `/responses` route | Yes | `ResponsesEndpointTests.PostResponses_DualEndpointModel_PrefersNativeResponsesRoute` |
| `/chat/completions` | `/chat/completions` capable | Plain text proxy | Yes | `HealthAndProxyEndpointTests.PostChatCompletions_ChatOnlyModel_ProxiesPlainTextResponse` |
| `/chat/completions` | `/responses` only | Plain text translation | Yes | `CopilotClientTests.ChatAsync_WhenModelSupportsResponsesOnly_MapsToolChoiceAndTools` |
| `/chat/completions` | `/responses` only | Tool round-trip preservation | Yes | `CopilotClientTests.ChatAsync_WhenResponsesModelReceivesToolConversation_PreservesToolCallContext` |
| `/chat/completions` | Both `/responses` and `/chat/completions` | Prefer native `/chat/completions` route | Yes | `CopilotClientTests.ChatAsync_WhenModelSupportsChatCompletions_PrefersNativeChatRoute` |

## Chat Completions SSE (backlog/003)

| Surface | Upstream model capability | Scenario | Covered | Test |
| --- | --- | --- | --- | --- |
| `/chat/completions` | `/chat/completions` capable | SSE streaming pass-through | Yes | `ChatCompletionsServiceTests.Stream_ChatCapableModel_PassesThroughSseChunks` |
| `/chat/completions` | `/responses` only | SSE streaming translation | Yes | `ChatCompletionsServiceTests.Stream_ResponsesOnlyModel_TranslatesResponsesEventsToChunks` |
| `/chat/completions` | `/responses` only | SSE streaming tool round-trip | Yes | `ChatCompletionsServiceTests.Stream_ResponsesOnlyModel_WithTools_TranslatesToolCallStream` |
| `/chat/completions` | Both | SSE streaming prefers native `/chat/completions` | Yes | `ChatCompletionsServiceTests.Stream_DualEndpointModel_PrefersNativeChatRoute` |
| `/chat/completions` | `/chat/completions` capable | SSE streaming with tools | Yes | `ChatCompletionsServiceTests.Stream_ChatCapableModel_WithTools_StreamsToolCalls` |

## Notes

- `/chat/completions` streaming uses `ChatCompletionsService` in Core, which routes and translates identically to `ResponsesService`. For responses-only models, `ResponsesStreamToChatTranslator` converts Responses SSE events into Chat Completions SSE chunks.
- `/chat/completions -> /responses` behavior is validated in `CopilotClientTests`, not endpoint integration tests, because the current integration harness replaces `IModelProvider` and cannot exercise `CopilotClient.ChatAsync()`.
- `/responses -> /chat/completions` behavior is validated in `ResponsesEndpointTests`, where the endpoint contract and translated request shapes are observable through the fake provider.
- Validation command used after completing the matrix:
  - `dotnet test tests\llm-svc.Tests\llm-svc.Tests.csproj --filter "Category!=Smoke" --no-restore`
