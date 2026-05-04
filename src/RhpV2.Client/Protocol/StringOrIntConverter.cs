using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace RhpV2.Client.Protocol;

/// <summary>
/// Reads a JSON value that the wire may quote or leave unquoted into a
/// <c>string?</c> property.  Real xrouter is inconsistent about the
/// <c>port</c> field across message types — <c>accept.port</c> arrives
/// as a JSON string ("2"), <c>recv.port</c> in TRACE mode arrives as a
/// JSON number (1), and <c>recv.port</c> in DGRAM mode arrives as a
/// string again.  Rather than expose three different property types we
/// normalise everything to <c>string?</c> at the boundary.
///
/// On write we always emit a JSON string — that's what xrouter accepts
/// on the request side too (<c>open.port</c>, <c>bind.port</c>).
/// </summary>
public sealed class StringOrIntConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var i) =>
                i.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number =>
                reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException(
                $"Unexpected token {reader.TokenType} when reading string-or-int."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
