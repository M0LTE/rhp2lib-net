using System.IO;
using System.Text;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;
using Xunit;

namespace RhpV2.Client.Tests;

public class FramingTests
{
    [Fact]
    public async Task WriteThenRead_RoundTrips_Payload()
    {
        var ms = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"auth\"}");
        await RhpFraming.WriteFrameAsync(ms, payload);

        ms.Position = 0;
        var got = await RhpFraming.ReadFrameAsync(ms);
        Assert.NotNull(got);
        Assert.Equal(payload, got);
    }

    [Fact]
    public void Header_IsBigEndian_TwoBytes()
    {
        var ms = new MemoryStream();
        var payload = new byte[300]; // > 255 to verify both header bytes are used
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;

        RhpFraming.WriteFrame(ms, payload);
        var bytes = ms.ToArray();

        Assert.Equal(0x01, bytes[0]); // 300 = 0x012C — big-endian high byte
        Assert.Equal(0x2C, bytes[1]);
        Assert.Equal(2 + 300, bytes.Length);
    }

    [Fact]
    public async Task ReadFrame_ReturnsNull_AtCleanEndOfStream()
    {
        var empty = new MemoryStream(Array.Empty<byte>());
        var got = await RhpFraming.ReadFrameAsync(empty);
        Assert.Null(got);
    }

    [Fact]
    public async Task ReadFrame_Throws_OnTruncatedHeader()
    {
        var ms = new MemoryStream(new byte[] { 0x01 });
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await RhpFraming.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task ReadFrame_Throws_OnTruncatedBody()
    {
        var ms = new MemoryStream(new byte[] { 0x00, 0x10, (byte)'a', (byte)'b' });
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await RhpFraming.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task ReadFrame_HandlesZeroLengthBody()
    {
        var ms = new MemoryStream(new byte[] { 0x00, 0x00 });
        var got = await RhpFraming.ReadFrameAsync(ms);
        Assert.NotNull(got);
        Assert.Empty(got!);
    }

    [Fact]
    public async Task WriteFrame_Rejects_OversizePayload()
    {
        var ms = new MemoryStream();
        var huge = new byte[RhpFraming.MaxPayloadLength + 1];
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await RhpFraming.WriteFrameAsync(ms, huge));
    }

    [Fact]
    public async Task ReadFrame_Reassembles_AcrossPartialReads()
    {
        // Build a frame and then expose it through a byte-at-a-time stream.
        var inner = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("hello, world");
        RhpFraming.WriteFrame(inner, payload);

        var trickle = new TrickleStream(inner.ToArray());
        var got = await RhpFraming.ReadFrameAsync(trickle);
        Assert.Equal(payload, got);
    }

    /// <summary>Stream that hands out one byte per Read call to exercise loops.</summary>
    private sealed class TrickleStream : Stream
    {
        private readonly byte[] _buf;
        private int _pos;
        public TrickleStream(byte[] buf) { _buf = buf; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _buf.Length) return 0;
            buffer[offset] = _buf[_pos++];
            return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _buf.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
