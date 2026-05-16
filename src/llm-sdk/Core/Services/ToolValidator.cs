using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Core.Services;

public static class ToolValidator
{
    public static ToolValidationResult Validate(ToolDefinition tool, string argumentsJson)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(argumentsJson);

        using var arguments = ParseArguments(argumentsJson);
        if (arguments is null)
        {
            return Invalid(["arguments must be valid JSON"]);
        }

        if (tool.Parameters is null)
        {
            return Valid();
        }

        var errors = new List<string>();
        ValidateObject(tool.Parameters.Value, arguments.RootElement, errors);
        return errors.Count == 0 ? Valid() : Invalid(errors);
    }

    public static ToolResultContent ToErrorResult(this ToolValidationResult result, string toolCallId)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);

        var message = result.Errors.Count == 0
            ? "Tool argument validation failed."
            : $"Tool argument validation failed: {string.Join("; ", result.Errors)}";
        return new ToolResultContent(toolCallId, message, IsError: true);
    }

    private static JsonDocument? ParseArguments(string argumentsJson)
    {
        try
        {
            return JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateObject(JsonElement schema, JsonElement value, List<string> errors)
    {
        if (GetSchemaType(schema) is "object" && value.ValueKind != JsonValueKind.Object)
        {
            errors.Add("arguments must be object");
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var required in GetRequiredProperties(schema))
        {
            if (!value.TryGetProperty(required, out _))
            {
                errors.Add($"{required} is required");
            }
        }

        var properties = TryGetObjectProperty(schema, "properties");
        var additionalPropertiesAllowed = !schema.TryGetProperty("additionalProperties", out var additionalProperties) ||
                                          additionalProperties.ValueKind != JsonValueKind.False;

        foreach (var argument in value.EnumerateObject())
        {
            if (properties is null || !properties.Value.TryGetProperty(argument.Name, out var propertySchema))
            {
                if (!additionalPropertiesAllowed)
                {
                    errors.Add($"{argument.Name} is not allowed");
                }

                continue;
            }

            ValidateProperty(argument.Name, propertySchema, argument.Value, errors);
        }
    }

    private static void ValidateProperty(string name, JsonElement schema, JsonElement value, List<string> errors)
    {
        var type = GetSchemaType(schema);
        if (type is not null && !MatchesType(value, type))
        {
            errors.Add($"{name} must be {type}");
            return;
        }

        if (schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array)
        {
            var allowed = enumValues.EnumerateArray().ToArray();
            if (!allowed.Any(allowedValue => JsonElementEquals(allowedValue, value)))
            {
                errors.Add($"{name} must be one of: {string.Join(", ", allowed.Select(FormatEnumValue))}");
            }
        }
    }

    private static string? GetSchemaType(JsonElement schema) =>
        schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    private static JsonElement? TryGetObjectProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static IEnumerable<string> GetRequiredProperties(JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
            {
                yield return name;
            }
        }
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static bool JsonElementEquals(JsonElement left, JsonElement right) =>
        string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static string FormatEnumValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static ToolValidationResult Valid() => new(true, []);

    private static ToolValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}
