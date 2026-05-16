using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ToolValidatorTests
{
    [Fact]
    public void Validate_WithValidArguments_ReturnsValid()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"city":"London","units":"metric","days":3}""");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsParseError()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"city":""");

        Assert.False(result.IsValid);
        Assert.Contains("arguments must be valid JSON", result.Errors);
    }

    [Fact]
    public void Validate_WithMissingRequiredProperty_ReturnsFieldError()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"units":"metric"}""");

        Assert.False(result.IsValid);
        Assert.Contains("city is required", result.Errors);
    }

    [Fact]
    public void Validate_WithWrongType_ReturnsFieldError()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"city":42,"units":"metric"}""");

        Assert.False(result.IsValid);
        Assert.Contains("city must be string", result.Errors);
    }

    [Fact]
    public void Validate_WithAdditionalPropertyWhenDisallowed_ReturnsFieldError()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"city":"London","units":"metric","country":"GB"}""");

        Assert.False(result.IsValid);
        Assert.Contains("country is not allowed", result.Errors);
    }

    [Fact]
    public void Validate_WithEnumMismatch_ReturnsFieldError()
    {
        var tool = CreateWeatherTool();

        var result = ToolValidator.Validate(tool, """{"city":"London","units":"kelvin"}""");

        Assert.False(result.IsValid);
        Assert.Contains("units must be one of: metric, imperial", result.Errors);
    }

    [Fact]
    public void Validate_WithoutSchema_ReturnsValidForValidJson()
    {
        var tool = new ToolDefinition("noop");

        var result = ToolValidator.Validate(tool, """{"anything":true}""");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ToErrorResult_ReturnsToolResultContentMarkedAsError()
    {
        var result = new ToolValidationResult(false, ["city is required", "units must be string"]);

        var content = result.ToErrorResult("call_1");

        Assert.Equal("call_1", content.ToolCallId);
        Assert.True(content.IsError);
        Assert.Equal("Tool argument validation failed: city is required; units must be string", content.Output);
    }

    private static ToolDefinition CreateWeatherTool()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "city" },
            additionalProperties = false,
            properties = new
            {
                city = new { type = "string" },
                units = new
                {
                    type = "string",
                    @enum = new[] { "metric", "imperial" },
                },
                days = new { type = "integer" },
            },
        }, JsonDefaults.Web);

        return new ToolDefinition("get_weather", "Gets the weather.", schema, Strict: true);
    }
}
