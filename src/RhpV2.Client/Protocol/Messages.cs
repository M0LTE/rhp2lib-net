using System.Text.Json.Serialization;

namespace RhpV2.Client.Protocol;

/// <summary>
/// Base class for all RHPv2 messages.  Concrete subclasses correspond to
/// the discriminator values defined in <see cref="RhpMessageType"/>.
/// </summary>
public abstract class RhpMessage
{
    /// <summary>The wire-format <c>type</c> discriminator for this message.</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>
    /// Optional request id used to correlate replies.  When omitted, the
    /// server only replies on error per the spec.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    /// <summary>
    /// Asynchronous notifications carry a server-assigned sequence number
    /// (RECV, ACCEPT, STATUS-from-server, CLOSE-from-server).
    /// </summary>
    [JsonPropertyName("seqno")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seqno { get; set; }
}

// ---------------------------------------------------------------------------
//  Authentication
// ---------------------------------------------------------------------------

public sealed class AuthMessage : RhpMessage
{
    public override string Type => RhpMessageType.Auth;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("pass")]
    public string Pass { get; set; } = string.Empty;
}

public sealed class AuthReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.AuthReply;

    /// <summary>Spec uses "errCode" (capital C) for AUTHREPLY.</summary>
    [JsonPropertyName("errCode")]
    public int ErrCode { get; set; }

    [JsonPropertyName("errText")]
    public string? ErrText { get; set; }
}

// ---------------------------------------------------------------------------
//  Combined OPEN (active or passive) — the high-level API of RHPv2
// ---------------------------------------------------------------------------

public sealed class OpenMessage : RhpMessage
{
    public override string Type => RhpMessageType.Open;

    [JsonPropertyName("pfam")]
    public string Pfam { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Port { get; set; }

    [JsonPropertyName("local")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Local { get; set; }

    [JsonPropertyName("remote")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Remote { get; set; }

    [JsonPropertyName("flags")]
    public int Flags { get; set; }
}

public sealed class OpenReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.OpenReply;

    [JsonPropertyName("handle")]
    public int Handle { get; set; }

    [JsonPropertyName("errcode")]
    public int ErrCode { get; set; }

    [JsonPropertyName("errtext")]
    public string? ErrText { get; set; }
}

// ---------------------------------------------------------------------------
//  BSD-style socket lifecycle
// ---------------------------------------------------------------------------

public sealed class SocketMessage : RhpMessage
{
    public override string Type => RhpMessageType.Socket;
    [JsonPropertyName("pfam")] public string Pfam { get; set; } = string.Empty;
    [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
}

public sealed class SocketReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.SocketReply;
    [JsonPropertyName("handle")] public int? Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

public sealed class BindMessage : RhpMessage
{
    public override string Type => RhpMessageType.Bind;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("local")] public string Local { get; set; } = string.Empty;
    [JsonPropertyName("port")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Port { get; set; }
}

public sealed class BindReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.BindReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

public sealed class ListenMessage : RhpMessage
{
    public override string Type => RhpMessageType.Listen;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("flags")] public int Flags { get; set; }
}

public sealed class ListenReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.ListenReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

public sealed class ConnectMessage : RhpMessage
{
    public override string Type => RhpMessageType.Connect;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("remote")] public string Remote { get; set; } = string.Empty;
}

public sealed class ConnectReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.ConnectReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

// ---------------------------------------------------------------------------
//  Data transfer
// ---------------------------------------------------------------------------

public sealed class SendMessage : RhpMessage
{
    public override string Type => RhpMessageType.Send;

    [JsonPropertyName("handle")] public int Handle { get; set; }

    /// <summary>
    /// Payload — control characters JSON-escaped per spec.  Use the helpers
    /// on <see cref="RhpV2.Client.RhpDataEncoding"/> for binary data.
    /// </summary>
    [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;

    // DGRAM mode only:
    [JsonPropertyName("port")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Port { get; set; }
    [JsonPropertyName("local")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Local { get; set; }
    [JsonPropertyName("remote")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Remote { get; set; }
}

public sealed class SendReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.SendReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
    /// <summary>STREAM-mode connection status (CONNECTED|BUSY).</summary>
    [JsonPropertyName("status")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Status { get; set; }
}

public sealed class SendToMessage : RhpMessage
{
    public override string Type => RhpMessageType.SendTo;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;
    [JsonPropertyName("port")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Port { get; set; }
    [JsonPropertyName("local")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Local { get; set; }
    [JsonPropertyName("remote")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Remote { get; set; }
    [JsonPropertyName("tos")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Tos { get; set; }
}

public sealed class SendToReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.SendToReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

public sealed class RecvMessage : RhpMessage
{
    public override string Type => RhpMessageType.Recv;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;

    // DGRAM:
    [JsonPropertyName("port")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Port { get; set; }

    // RAW / TRACE:
    [JsonPropertyName("action")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Action { get; set; }
    [JsonPropertyName("srce")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Srce { get; set; }
    [JsonPropertyName("dest")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Dest { get; set; }
    [JsonPropertyName("ctrl")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Ctrl { get; set; }
    [JsonPropertyName("frametype")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FrameType { get; set; }
    [JsonPropertyName("rseq")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Rseq { get; set; }
    [JsonPropertyName("cr")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cr { get; set; }
    [JsonPropertyName("pf")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Pf { get; set; }
}

// ---------------------------------------------------------------------------
//  Notifications + status
// ---------------------------------------------------------------------------

public sealed class AcceptMessage : RhpMessage
{
    public override string Type => RhpMessageType.Accept;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("child")] public int Child { get; set; }
    [JsonPropertyName("remote")] public string? Remote { get; set; }
    [JsonPropertyName("local")] public string? Local { get; set; }
    [JsonPropertyName("port")] public int? Port { get; set; }
}

public sealed class StatusMessage : RhpMessage
{
    public override string Type => RhpMessageType.Status;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("flags")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Flags { get; set; }
}

public sealed class StatusReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.StatusReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}

public sealed class CloseMessage : RhpMessage
{
    public override string Type => RhpMessageType.Close;
    [JsonPropertyName("handle")] public int Handle { get; set; }
}

public sealed class CloseReplyMessage : RhpMessage
{
    public override string Type => RhpMessageType.CloseReply;
    [JsonPropertyName("handle")] public int Handle { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errtext")] public string? ErrText { get; set; }
}
