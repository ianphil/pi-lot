using CopilotLlm.Core.Models;

namespace CopilotLlm.Client;

public class CopilotLlmException : Exception
{
    public CopilotLlmException(
        string message,
        string? errorCode,
        int statusCode,
        string? errorType = null,
        string? param = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
        Param = param;
        StatusCode = statusCode;
    }

    public string? ErrorCode { get; }

    public string? ErrorType { get; }

    public string? Param { get; }

    public int StatusCode { get; }
}

public sealed class ModelNotFoundException : CopilotLlmException
{
    public ModelNotFoundException(
        string message,
        string? errorType = null,
        string? param = null)
        : base(message, ErrorCodes.ModelNotFound, 404, errorType, param)
    {
    }
}

public sealed class AuthenticationException : CopilotLlmException
{
    public AuthenticationException(
        string message,
        string? errorCode = ErrorCodes.AuthError,
        string? errorType = null,
        string? param = null,
        int statusCode = 401)
        : base(message, errorCode, statusCode, errorType, param)
    {
    }
}

public sealed class RateLimitException : CopilotLlmException
{
    public RateLimitException(
        string message,
        TimeSpan? retryAfter = null,
        string? errorCode = null,
        string? errorType = null,
        string? param = null,
        int statusCode = 429)
        : base(message, errorCode, statusCode, errorType, param)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
