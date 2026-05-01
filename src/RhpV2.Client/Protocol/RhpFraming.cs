using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RhpV2.Client.Protocol;

/// <summary>
/// Implements RHPv2 framing: a 2-byte big-endian length prefix followed by
/// a JSON payload of that length (max 65535 bytes per spec).
///
/// <code>
/// .---------------------------.
/// | lenH | lenL | RHP Message |
/// '---------------------------'
/// Bytes:  1       1    &lt;--- len ---&gt;
/// </code>
/// </summary>
public static class RhpFraming
{
    /// <summary>The maximum payload size implied by the 2-byte length field.</summary>
    public const int MaxPayloadLength = 0xFFFF;

    /// <summary>
    /// Write a length-prefixed frame to <paramref name="output"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the payload is too large.</exception>
    public static async Task WriteFrameAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
    {
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentException(
                $"RHP payload exceeds 16-bit length field ({payload.Length} > {MaxPayloadLength}).",
                nameof(payload));

        var header = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);
        await output.WriteAsync(header, ct).ConfigureAwait(false);
        await output.WriteAsync(payload, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous version of <see cref="WriteFrameAsync"/>, useful for tests.
    /// </summary>
    public static void WriteFrame(Stream output, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentException(
                $"RHP payload exceeds 16-bit length field ({payload.Length} > {MaxPayloadLength}).",
                nameof(payload));

        Span<byte> header = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);
        output.Write(header);
        output.Write(payload);
        output.Flush();
    }

    /// <summary>
    /// Read one length-prefixed frame from <paramref name="input"/>.
    /// Returns <c>null</c> at clean end-of-stream (zero bytes before the header).
    /// Throws <see cref="EndOfStreamException"/> on a partial frame.
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(
        Stream input,
        CancellationToken ct = default)
    {
        var header = new byte[2];
        var firstRead = await input.ReadAsync(header.AsMemory(0, 2), ct).ConfigureAwait(false);
        if (firstRead == 0) return null;
        if (firstRead < 2)
        {
            await ReadExactlyAsync(input, header.AsMemory(firstRead, 2 - firstRead), ct)
                .ConfigureAwait(false);
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (length == 0) return Array.Empty<byte>();

        var buffer = new byte[length];
        await ReadExactlyAsync(input, buffer.AsMemory(), ct).ConfigureAwait(false);
        return buffer;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken ct)
    {
        int total = 0;
        while (total < destination.Length)
        {
            var read = await stream
                .ReadAsync(destination.Slice(total), ct)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException(
                    $"Stream ended after {total}/{destination.Length} bytes of RHP frame.");
            total += read;
        }
    }
}
