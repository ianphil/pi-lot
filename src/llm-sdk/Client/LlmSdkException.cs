using LlmSdk.Core.Models;

namespace LlmSdk.Client;

/// <summary>
/// Base exception for SDK failures returned by the Copilot API or SDK transport.
/// </summary>
public class LlmSdkException : Exception
{
    /// <summary>
    /// Initializes a new SDK exception.
    /// </summary>
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

    /// <summary>
    /// Provider or SDK error code, when available.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Provider error type, when available.
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// Request parameter associated with the error, when available.
    /// </summary>
    public string? Param { get; }

    /// <summary>
    /// HTTP status code associated with the failure.
    /// </summary>
    public int StatusCode { get; }
}

/// <summary>
/// Exception thrown when a requested model is not available.
/// </summary>
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

/// <summary>
/// Exception thrown when Copilot authentication fails.
/// </summary>
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

/// <summary>
/// Exception thrown when a request is rate limited.
/// </summary>
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

    /// <summary>
    /// Suggested delay before retrying, when provided by the upstream service.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Exception thrown when a request exceeds the selected model's context window.
/// </summary>
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

    /// <summary>
    /// Detected context-window size in tokens, when available.
    /// </summary>
    public int? ContextWindow { get; }

    /// <summary>
    /// Detected input-token count in tokens, when available.
    /// </summary>
    public int? InputTokens { get; }
}
