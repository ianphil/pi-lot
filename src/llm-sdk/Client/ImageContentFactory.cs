using LlmSdk.Core.Models;

namespace LlmSdk.Client;

/// <summary>
/// Creates validated image content blocks for portable context requests.
/// </summary>
public static class ImageContentFactory
{
    /// <summary>
    /// The default maximum image payload size, in bytes.
    /// </summary>
    public const long DefaultMaxBytes = 20L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> MediaTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        };

    private static readonly HashSet<string> SupportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
    };

    /// <summary>
    /// Reads an image file and creates an <see cref="ImageContent"/> block.
    /// </summary>
    /// <param name="path">Path to a PNG, JPEG, GIF, or WebP image.</param>
    /// <param name="maxBytes">Maximum allowed file size in bytes.</param>
    /// <returns>An image content block with base64-encoded data.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="path"/> does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the extension or file size is unsupported.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is not positive.</exception>
    public static ImageContent FromFile(string path, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateMaxBytes(maxBytes);

        var mediaType = GetMediaType(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Image file was not found.", path);
        }

        if (info.Length > maxBytes)
        {
            throw new InvalidOperationException($"Image file '{path}' is {info.Length} bytes, exceeding the {maxBytes} byte limit.");
        }

        return FromBytes(File.ReadAllBytes(path), mediaType, maxBytes);
    }

    /// <summary>
    /// Creates an <see cref="ImageContent"/> block from image bytes.
    /// </summary>
    /// <param name="bytes">The raw image bytes.</param>
    /// <param name="mediaType">The media type. Supported values are image/png, image/jpeg, image/gif, and image/webp.</param>
    /// <param name="maxBytes">Maximum allowed content size in bytes.</param>
    /// <returns>An image content block with base64-encoded data.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the media type or size is unsupported.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is not positive.</exception>
    public static ImageContent FromBytes(ReadOnlySpan<byte> bytes, string mediaType, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ValidateMaxBytes(maxBytes);
        ValidateMediaType(mediaType);

        if (bytes.Length > maxBytes)
        {
            throw new InvalidOperationException($"Image content is {bytes.Length} bytes, exceeding the {maxBytes} byte limit.");
        }

        return new ImageContent(mediaType, Convert.ToBase64String(bytes));
    }

    private static string GetMediaType(string path)
    {
        var extension = Path.GetExtension(path);
        if (MediaTypesByExtension.TryGetValue(extension, out var mediaType))
        {
            return mediaType;
        }

        throw new InvalidOperationException($"Unsupported image file extension '{extension}'. Supported types are PNG, JPEG, GIF, and WebP.");
    }

    private static void ValidateMediaType(string mediaType)
    {
        if (!SupportedMediaTypes.Contains(mediaType))
        {
            throw new InvalidOperationException($"Unsupported image media type '{mediaType}'. Supported types are image/png, image/jpeg, image/gif, and image/webp.");
        }
    }

    private static void ValidateMaxBytes(long maxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Maximum image size must be greater than zero.");
        }
    }
}
