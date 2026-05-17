using System.Text;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SseChunkParserUnicodeTests
{
    [Theory]
    [InlineData("🚀")]
    [InlineData("日本語")]
    [InlineData("👩‍💻")]
    public void ParseChunk_WhenUtf8BytesAreSplit_EmitsTextIntact(string text)
    {
        var parsed = ParseByteByByte($"event: message\ndata: {text}\n\n");

        var chunk = Assert.Single(parsed);
        Assert.Equal("message", chunk.EventName);
        Assert.Equal(text, chunk.Data);
        Assert.DoesNotContain("\uFFFD", chunk.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseChunk_WhenTrailingHighSurrogateIsSplit_BuffersUntilLowSurrogateArrives()
    {
        var parser = new SseChunkParser();

        var first = parser.ParseChunk("event: message\ndata: \ud83d\n\n");
        var second = parser.ParseChunk("data: \ude80\n\n");

        Assert.Null(first);
        var chunk = Assert.NotNull(second);
        Assert.Equal("message", chunk.EventName);
        Assert.Equal("🚀", chunk.Data);
        Assert.DoesNotContain("\uFFFD", chunk.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseChunk_WhenDataContainsLoneSurrogates_DoesNotEmitInvalidSurrogates()
    {
        var chunk = SseChunkParser.Parse("data: a\ude80b\ud83dx\n\n");

        var parsed = Assert.NotNull(chunk);
        Assert.Equal("abx", parsed.Data);
        Assert.DoesNotContain(parsed.Data, char.IsSurrogate);
    }

    private static IReadOnlyList<ParsedSseChunk> ParseByteByByte(string sse)
    {
        var parser = new SseChunkParser();
        var bytes = Encoding.UTF8.GetBytes(sse);
        var parsed = new List<ParsedSseChunk>();
        var chars = new char[2];
        using var reader = new StreamReader(
            new OneByteAtATimeStream(bytes),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1);

        int charsUsed;
        while ((charsUsed = reader.Read(chars, 0, 1)) > 0)
        {
            AddParsed(parser.ParseChunk(new string(chars, 0, charsUsed)), parsed);
        }

        return parsed;
    }

    private static void AddParsed(ParsedSseChunk? chunk, List<ParsedSseChunk> parsed)
    {
        if (chunk is not null)
        {
            parsed.Add(chunk.Value);
        }
    }

    private sealed class OneByteAtATimeStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= bytes.Length || count == 0)
            {
                return 0;
            }

            buffer[offset] = bytes[_position++];
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
