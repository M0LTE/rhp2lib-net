using System.Text;

namespace RhpV2.Client.Protocol;

/// <summary>
/// Helpers for moving binary payloads through the JSON <c>data</c> field.
///
/// The spec says: "Total message size ≤ 65535 bytes. Control characters
/// JSON-escaped."  In practice this means we encode bytes as Latin-1 (ISO
/// 8859-1) so each byte is one code unit and the System.Text.Json escape
/// machinery turns the control characters into JSON escape sequences for us.
/// </summary>
public static class RhpDataEncoding
{
    /// <summary>Encode a byte payload as a Latin-1 string for JSON transport.</summary>
    public static string ToWireString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;
#if NET8_0_OR_GREATER
        return Encoding.Latin1.GetString(bytes);
#else
        return Encoding.GetEncoding(28591).GetString(bytes.ToArray());
#endif
    }

    /// <summary>Decode a Latin-1 wire string back to bytes.</summary>
    public static byte[] FromWireString(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
#if NET8_0_OR_GREATER
        return Encoding.Latin1.GetBytes(s);
#else
        return Encoding.GetEncoding(28591).GetBytes(s);
#endif
    }
}
