using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class OverflowDetectorTests
{
    [Theory]
    [InlineData("This model's maximum context length is 128000 tokens. However, you requested 131000 tokens.")]
    [InlineData("input is too long")]
    [InlineData("prompt is too long")]
    [InlineData("This model's maximum context length was exceeded.")]
    [InlineData("too many tokens in request")]
    [InlineData("Please reduce the length of the messages.")]
    public void IsOverflow_WhenKnownOverflowPhraseMatches_ReturnsTrue(string message)
    {
        Assert.True(OverflowDetector.IsOverflow(400, message, null));
    }

    [Fact]
    public void IsOverflow_WhenErrorCodeIsContextLengthExceeded_ReturnsTrue()
    {
        Assert.True(OverflowDetector.IsOverflow(422, "Request failed.", ErrorCodes.ContextLengthExceeded));
    }

    [Theory]
    [InlineData(400, "model is required", "invalid_request")]
    [InlineData(400, "input is too long-form for this unrelated field", "invalid_request")]
    [InlineData(429, "input is too long", "rate_limited")]
    public void IsOverflow_WhenNotContextOverflow_ReturnsFalse(int statusCode, string message, string code)
    {
        Assert.False(OverflowDetector.IsOverflow(statusCode, message, code));
    }

    [Fact]
    public void TryExtractTokens_WhenMessageContainsWindowAndRequestedTokens_ReturnsCounts()
    {
        var (window, input) = OverflowDetector.TryExtractTokens(
            "This model's maximum context length is 128,000 tokens. However, you requested 131 250 tokens.");

        Assert.Equal(128000, window);
        Assert.Equal(131250, input);
    }

    [Theory]
    [InlineData(96, 100, StopReason.Length, true)]
    [InlineData(95, 100, StopReason.Length, false)]
    [InlineData(96, 100, StopReason.Stop, false)]
    [InlineData(96, null, StopReason.Length, false)]
    public void IsSilentTruncation_WhenNearWindowAndLengthStop_ReturnsExpected(
        long inputTokens,
        int? contextWindow,
        StopReason stopReason,
        bool expected)
    {
        Assert.Equal(expected, OverflowDetector.IsSilentTruncation(inputTokens, contextWindow, stopReason));
    }
}
