using System.Text.Json;
using CopilotLlm.Client;
using CopilotLlm.Core.Models;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CopilotLlmExceptionTests
{
    [Fact]
    public void CopilotLlmException_CarriesStructuredErrorProperties()
    {
        var exception = new CopilotLlmException(
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
    public void ModelNotFoundException_IsACopilotLlmException()
    {
        CopilotLlmException exception = new ModelNotFoundException(
            "The requested model does not exist.",
            ErrorTypes.InvalidRequestError,
            "model");

        Assert.Equal(ErrorCodes.ModelNotFound, exception.ErrorCode);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public void AuthenticationException_IsACopilotLlmException()
    {
        CopilotLlmException exception = new AuthenticationException(
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

        var exception = CopilotLlmExceptionFactory.Create(404, body);

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

        var exception = CopilotLlmExceptionFactory.Create(401, body);

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

        var exception = CopilotLlmExceptionFactory.Create(429, body);

        var typed = Assert.IsType<RateLimitException>(exception);
        Assert.Equal(TimeSpan.FromSeconds(12), typed.RetryAfter);
        Assert.Equal("rate_limited", typed.ErrorCode);
        Assert.Equal("too_many_requests", typed.ErrorType);
    }

    [Fact]
    public void Create_WhenErrorIsUnknown_ReturnsBaseCopilotLlmException()
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

        var exception = CopilotLlmExceptionFactory.Create(500, body);

        Assert.IsType<CopilotLlmException>(exception);
        Assert.Equal("upstream_error", exception.ErrorCode);
        Assert.Equal(ErrorTypes.ServerError, exception.ErrorType);
        Assert.Equal(500, exception.StatusCode);
    }
}
