using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LlmSdkExceptionTests
{
    [Fact]
    public void LlmSdkException_CarriesStructuredErrorProperties()
    {
        var exception = new LlmSdkException(
            "The requested model does not exist.",
            ErrorCodes.ModelNotFound,
            404,
            ErrorTypes.InvalidRequestError,
            "model");

        Assert.Equal("The requested model does not exist.", exception.Message);
        Assert.Equal(ErrorCodes.ModelNotFound, exception.ErrorCode);
        Assert.Equal(ErrorTypes.InvalidRequestError, exception.ErrorType);
        Assert.Equal("model", exception.Param);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public void ModelNotFoundException_IsALlmSdkException()
    {
        LlmSdkException exception = new ModelNotFoundException(
            "The requested model does not exist.",
            ErrorTypes.InvalidRequestError,
            "model");

        Assert.Equal(ErrorCodes.ModelNotFound, exception.ErrorCode);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public void AuthenticationException_IsALlmSdkException()
    {
        LlmSdkException exception = new AuthenticationException(
            "Not authenticated.",
            ErrorCodes.AuthError,
            "error",
            "authorization");

        Assert.Equal(ErrorCodes.AuthError, exception.ErrorCode);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void RateLimitException_CarriesRetryAfterTimeSpan()
    {
        var exception = new RateLimitException(
            "Slow down.",
            TimeSpan.FromSeconds(12),
            "rate_limited",
            ErrorTypes.TooManyRequests);

        Assert.Equal(TimeSpan.FromSeconds(12), exception.RetryAfter);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    public void ContextOverflowException_CarriesTokenDetails()
    {
        var exception = new ContextOverflowException(
            "Context length exceeded.",
            contextWindow: 128000,
            inputTokens: 131250,
            statusCode: 400);

        Assert.Equal(ErrorCodes.ContextLengthExceeded, exception.ErrorCode);
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(128000, exception.ContextWindow);
        Assert.Equal(131250, exception.InputTokens);
    }

    [Fact]
    public void Create_WhenErrorCodeIsModelNotFound_ReturnsModelNotFoundException()
    {
        var body = JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "The requested model does not exist.",
                Type = ErrorTypes.InvalidRequestError,
                Param = "model",
                Code = ErrorCodes.ModelNotFound,
            },
        }, JsonDefaults.Web);

        var exception = LlmSdkExceptionFactory.Create(404, body);

        var typed = Assert.IsType<ModelNotFoundException>(exception);
        Assert.Equal("The requested model does not exist.", typed.Message);
        Assert.Equal(ErrorTypes.InvalidRequestError, typed.ErrorType);
        Assert.Equal("model", typed.Param);
    }

    [Fact]
    public void Create_WhenStatusCodeIs401_ReturnsAuthenticationException()
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

        var exception = LlmSdkExceptionFactory.Create(401, body);

        var typed = Assert.IsType<AuthenticationException>(exception);
        Assert.Equal(ErrorCodes.AuthError, typed.ErrorCode);
        Assert.Equal("error", typed.ErrorType);
        Assert.Equal(401, typed.StatusCode);
    }

    [Fact]
    public void Create_WhenStatusCodeIs429AndRetryAfterIsPresent_ReturnsRateLimitException()
    {
        const string body = """
            {
              "error": {
                "message": "Slow down.",
                "type": "too_many_requests",
                "code": "rate_limited",
                "retry_after": 12
              }
            }
            """;

        var exception = LlmSdkExceptionFactory.Create(429, body);

        var typed = Assert.IsType<RateLimitException>(exception);
        Assert.Equal(TimeSpan.FromSeconds(12), typed.RetryAfter);
        Assert.Equal("rate_limited", typed.ErrorCode);
        Assert.Equal("too_many_requests", typed.ErrorType);
    }

    [Fact]
    public void Create_WhenErrorIsContextOverflow_ReturnsContextOverflowException()
    {
        const string body = """
            {
              "error": {
                "message": "This model's maximum context length is 128,000 tokens. However, you requested 131,250 tokens.",
                "type": "invalid_request_error",
                "code": "context_length_exceeded"
              }
            }
            """;

        var exception = LlmSdkExceptionFactory.Create(400, body);

        var typed = Assert.IsType<ContextOverflowException>(exception);
        Assert.Equal(128000, typed.ContextWindow);
        Assert.Equal(131250, typed.InputTokens);
        Assert.Equal(ErrorTypes.InvalidRequestError, typed.ErrorType);
    }

    [Fact]
    public void Create_WhenErrorIsUnknown_ReturnsBaseLlmSdkException()
    {
        var body = JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Upstream exploded.",
                Type = ErrorTypes.ServerError,
                Code = "upstream_error",
            },
        }, JsonDefaults.Web);

        var exception = LlmSdkExceptionFactory.Create(500, body);

        Assert.IsType<LlmSdkException>(exception);
        Assert.Equal("upstream_error", exception.ErrorCode);
        Assert.Equal(ErrorTypes.ServerError, exception.ErrorType);
        Assert.Equal(500, exception.StatusCode);
    }
}
