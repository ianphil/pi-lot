using LlmSdk.Client;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ImageContentFactoryTests
{
    [Fact]
    public void FromBytes_WithSupportedMediaType_Base64EncodesContent()
    {
        var image = ImageContentFactory.FromBytes([1, 2, 3], "image/png");

        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("AQID", image.Base64Data);
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".webp", "image/webp")]
    public void FromFile_WithSupportedExtension_DetectsMediaType(string extension, string expectedMediaType)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);

            var image = ImageContentFactory.FromFile(path);

            Assert.Equal(expectedMediaType, image.MediaType);
            Assert.Equal("AQID", image.Base64Data);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void FromFile_WhenFileExceedsMaxBytes_ThrowsClearException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);

            var exception = Assert.Throws<InvalidOperationException>(() => ImageContentFactory.FromFile(path, maxBytes: 2));

            Assert.Contains("exceeding the 2 byte limit", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void FromFile_WithUnsupportedExtension_ThrowsClearException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ImageContentFactory.FromFile("image.bmp"));

        Assert.Contains("Unsupported image file extension '.bmp'", exception.Message, StringComparison.Ordinal);
    }
}
