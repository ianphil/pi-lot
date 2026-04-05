#pragma warning disable OPENAI001

using System.ClientModel;
using System.Text.Json;
using CopilotLlm.Core.Models;
using OpenAI;
using OpenAI.Responses;

namespace llm_svc.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SdkCompatibilityTests
{
    [Fact]
    public async Task ResponsesSdk_CanCallProxyUsingCustomEndpoint()
    {
        using var factory = new ResponsesWebApplicationFactory();
        factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat_sdk_123",
            Model = "claude-haiku-4.5",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Hello from OpenAI .NET SDK",
                    },
                    FinishReason = "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = 5,
                CompletionTokens = 6,
                TotalTokens = 11,
            },
        }, JsonDefaults.Web), 200);

        factory.UseKestrel(0);
        using var bootstrapClient = factory.CreateClient();

        var responsesClient = new ResponsesClient(
            new ApiKeyCredential("unused"),
            new OpenAIClientOptions
            {
                Endpoint = bootstrapClient.BaseAddress ?? throw new InvalidOperationException("A network base address is required."),
            });

        var options = new CreateResponseOptions
        {
            Model = "claude-haiku-4.5",
        };
        options.InputItems.Add(OpenAI.Responses.ResponseItem.CreateUserMessageItem("Say hello"));

        ResponseResult response = await responsesClient.CreateResponseAsync(options);

        var message = Assert.IsAssignableFrom<MessageResponseItem>(Assert.Single(response.OutputItems));
        Assert.Equal("assistant", message.Role.ToString().ToLowerInvariant());
        Assert.Equal("Hello from OpenAI .NET SDK", Assert.Single(message.Content).Text);
    }
}
