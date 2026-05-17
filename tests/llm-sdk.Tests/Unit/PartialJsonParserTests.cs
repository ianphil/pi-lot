using System.Text.Json;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PartialJsonParserTests
{
    [Fact]
    public void TryParse_WithEveryObjectPrefix_ReturnsValidJsonObject()
    {
        const string json = """{"url":"https://example.com","method":"GET"}""";

        for (var length = 1; length <= json.Length; length++)
        {
            var parsed = PartialJsonParser.TryParse(json[..length]);

            Assert.NotNull(parsed);
            Assert.Equal(JsonValueKind.Object, parsed.Value.ValueKind);
        }
    }

    [Fact]
    public void TryParse_WithGrowingObjectPrefix_PreservesCompletedProperties()
    {
        const string json = """{"url":"https://example.com","method":"GET"}""";
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);

        for (var length = 1; length <= json.Length; length++)
        {
            var parsed = PartialJsonParser.TryParse(json[..length]);

            Assert.NotNull(parsed);
            foreach (var property in parsed.Value.EnumerateObject())
            {
                seenProperties.Add(property.Name);
            }
        }

        Assert.Contains("url", seenProperties);
        Assert.Contains("method", seenProperties);
    }

    [Fact]
    public void TryParse_WithGarbage_ReturnsNull()
    {
        var parsed = PartialJsonParser.TryParse("not json");

        Assert.Null(parsed);
    }
}
