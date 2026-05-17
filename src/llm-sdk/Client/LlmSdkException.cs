using LlmSdk.Core.Models;

namespace LlmSdk.Client;

public class LlmSdkException : Exception
{
    public LlmSdkException(
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

public sealed class ModelNotFoundException : LlmSdkException
{
    public ModelNotFoundException(
        string message,
        string? errorType = null,
        string? param = null)
        : base(message, ErrorCodes.ModelNotFound, 404, errorType, param)
    {
    }
}

public sealed class AuthenticationException : LlmSdkException
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

public sealed class RateLimitException : LlmSdkException
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

public sealed class ContextOverflowException : LlmSdkException
{
    public ContextOverflowException(
        string message,
        int? contextWindow,
        int? inputTokens,
        string? errorCode = ErrorCodes.ContextLengthExceeded,
        string? errorType = null,
        string? param = null,
        int statusCode = 400,
        Exception? innerException = null)
        : base(message, errorCode, statusCode, errorType, param, innerException)
    {
        ContextWindow = contextWindow;
        InputTokens = inputTokens;
    }

    public int? ContextWindow { get; }

    public int? InputTokens { get; }
}
