using System.Text.Json;
using CopilotLlm.Client;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using CopilotLlm.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CopilotLlmClientTests
{
    [Fact]
    public async Task CreateResponseAsync_WithRequest_ReturnsDeserializedResponseOnSuccess()
    {
        var expected = CreateResponse("resp_123", "Hello from Copilot");
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(expected, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var response = await client.CreateResponseAsync(CreateResponseRequest());

        Assert.Equal(expected.Id, response.Id);
        Assert.Equal("Hello from Copilot", response.GetOutputText());
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateResponseAsync_WithRequest_ThrowsCopilotLlmExceptionOnErrorStatus()
    {
        var body = JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Model not found.",
                Type = ErrorTypes.InvalidRequestError,
                Param = "model",
                Code = ErrorCodes.ModelNotFound,
            },
        }, JsonDefaults.Web);
        var service = new StubResponsesService(ResponseHttpResult.FromBody(body, 404, "application/json"));
        var client = CreateClient(responsesService: service);

        var exception = await Assert.ThrowsAsync<ModelNotFoundException>(() => client.CreateResponseAsync(CreateResponseRequest()));

        Assert.Equal("Model not found.", exception.Message);
        Assert.Equal("model", exception.Param);
    }

    [Fact]
    public async Task CreateResponseAsync_WithModelAndInput_BuildsRequestAndReturnsResponse()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateResponse("resp_234", "Convenience"), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var response = await client.CreateResponseAsync("gpt-5.4-mini", "Hello!");

        Assert.Equal("resp_234", response.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Equal("Hello!", service.LastRequest?.Input.GetString());
        Assert.False(service.LastRequest?.Stream ?? false);
    }

    [Fact]
    public async Task CreateResponseAsync_WithMissingModel_UsesDefaultModel()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateResponse("resp_345", "Default model"), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(
            responsesService: service,
            options: new CopilotLlmOptions { DefaultModel = "claude-sonnet-4.5" });

        _ = await client.CreateResponseAsync(model: null, "Hello!");

        Assert.Equal("claude-sonnet-4.5", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateResponseAsync_WithMissingModelAndNoDefault_ThrowsArgumentException()
    {
        var client = CreateClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.CreateResponseAsync(model: null, "Hello!"));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithRequest_ReturnsDeserializedResponseOnSuccess()
    {
        var expected = new ChatCompletionResponse
        {
            Id = "chatcmpl_123",
            Model = "gpt-5.4-mini",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Hello from chat",
                    },
                },
            ],
        };
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(expected, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(chatService: service);

        var response = await client.CreateChatCompletionAsync(CreateChatCompletionRequest());

        Assert.Equal(expected.Id, response.Id);
        Assert.Equal("Hello from chat", response.GetMessageText());
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithRequest_ThrowsCopilotLlmExceptionOnError()
    {
        var body = JsonSerializer.Serialize(new OpenAIErrorResponse
        {
            Error = new OpenAIError
            {
                Message = "Not authenticated.",
                Type = "error",
                Code = ErrorCodes.AuthError,
            },
        }, JsonDefaults.Web);
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(body, 401, "application/json"));
        var client = CreateClient(chatService: service);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => client.CreateChatCompletionAsync(CreateChatCompletionRequest()));

        Assert.Equal(ErrorCodes.AuthError, exception.ErrorCode);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithModelAndMessage_BuildsRequestAndReturnsResponse()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chatcmpl_234",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Convenience chat",
                        },
                    },
                ],
            }, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(chatService: service);

        var response = await client.CreateChatCompletionAsync("gpt-5.4-mini", "Hello!");

        Assert.Equal("chatcmpl_234", response.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Single(service.LastRequest?.Messages ?? []);
        Assert.Equal("user", service.LastRequest?.Messages?[0].Role);
        Assert.Equal("Hello!", service.LastRequest?.Messages?[0].Content);
        Assert.False(service.LastRequest?.Stream ?? false);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsModelListFromModelListService()
    {
        var modelProvider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "gpt-5.4-mini",
                    Name = "GPT 5.4 Mini",
                    OwnedBy = "github-copilot",
                    SupportedEndpoints = ["/responses", "/chat/completions"],
                },
                new ModelDescriptor
                {
                    Id = "embeddings-only",
                    SupportedEndpoints = ["/embeddings"],
                },
            ],
        };
        var client = CreateClient(modelProvider: modelProvider);

        var models = await client.ListModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("gpt-5.4-mini", model.Id);
        Assert.NotNull(model.ProxySupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], model.ProxySupportedEndpoints);
    }

    private static CopilotLlmClient CreateClient(
        StubResponsesService? responsesService = null,
        StubChatCompletionsService? chatService = null,
        FakeModelProvider? modelProvider = null,
        CopilotLlmOptions? options = null)
    {
        return new CopilotLlmClient(
            responsesService ?? new StubResponsesService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            chatService ?? new StubChatCompletionsService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            new ModelListService(modelProvider ?? new FakeModelProvider()),
            Options.Create(options ?? new CopilotLlmOptions()));
    }

    private static CreateResponseRequest CreateResponseRequest(string? model = "gpt-5.4-mini", string input = "Hello!")
    {
        return new CreateResponseRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(input, JsonDefaults.Web),
        };
    }

    private static ChatCompletionRequest CreateChatCompletionRequest(string? model = "gpt-5.4-mini", string message = "Hello!")
    {
        return new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = message,
                },
            ],
        };
    }

    private static Response CreateResponse(string id, string text)
    {
        return new Response
        {
            Id = id,
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = text,
                        },
                    ],
                },
            ],
        };
    }

    private sealed class StubResponsesService(ResponseHttpResult result) : IResponsesService
    {
        public CreateResponseRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubChatCompletionsService(ResponseHttpResult result) : IChatCompletionsService
    {
        public ChatCompletionRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
