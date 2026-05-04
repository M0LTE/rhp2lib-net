namespace RhpV2.Client.Protocol;

/// <summary>
/// RHPv2 protocol families. Sent on the wire as the lowercase <c>pfam</c> value.
/// </summary>
public static class ProtocolFamily
{
    /// <summary>XRouter CLI / applications (layer 7).</summary>
    public const string Unix = "unix";
    /// <summary>TCP/UDP/ICMP/IP/DNS (layer 3/4).</summary>
    public const string Inet = "inet";
    /// <summary>AX.25, APRS, digipeating (layer 2).</summary>
    public const string Ax25 = "ax25";
    /// <summary>NetRom datagrams &amp; streams (layer 3/4).</summary>
    public const string NetRom = "netrom";
}

/// <summary>
/// RHPv2 socket modes. Sent on the wire as the lowercase <c>mode</c> value.
/// </summary>
public static class SocketMode
{
    /// <summary>Ordered, reliable octet stream.</summary>
    public const string Stream = "stream";
    /// <summary>Unreliable datagram.</summary>
    public const string Dgram = "dgram";
    /// <summary>Sequenced reliable packets (AX.25).</summary>
    public const string Seqpkt = "seqpkt";
    /// <summary>User-specified protocol.</summary>
    public const string Custom = "custom";
    /// <summary>Addresses + raw payload.</summary>
    public const string SemiRaw = "semiraw";
    /// <summary>Decoded headers + payload.</summary>
    public const string Trace = "trace";
    /// <summary>Complete raw packet.</summary>
    public const string Raw = "raw";
}

/// <summary>
/// Bitfield flags for the OPEN message (and LISTEN, where applicable).
/// </summary>
[Flags]
public enum OpenFlags
{
    /// <summary>Passive open (listen) — the default for most listeners.</summary>
    Passive = 0x00,
    /// <summary>Trace incoming frames (RAW/TRACE only).</summary>
    TraceIncoming = 0x01,
    /// <summary>Trace outgoing frames (RAW/TRACE only).</summary>
    TraceOutgoing = 0x02,
    /// <summary>Trace supervisory frames (TRACE only, AX.25).</summary>
    TraceSupervisory = 0x04,
    /// <summary>Active open — perform a connect.</summary>
    Active = 0x80,
}

/// <summary>
/// Status flags carried in STATUS / SENDREPLY messages.
/// </summary>
[Flags]
public enum StatusFlags
{
    None = 0,
    /// <summary>OK to accept on listeners.</summary>
    ConOk = 1,
    /// <summary>Downlink connected.</summary>
    Connected = 2,
    /// <summary>Not clear to send (flow control).</summary>
    Busy = 4,
}

/// <summary>
/// Canonical RHPv2 error codes (from PWP-0222 / PWP-0245).
/// </summary>
public static class RhpErrorCode
{
    public const int Ok = 0;
    public const int Unspecified = 1;
    public const int BadOrMissingType = 2;
    public const int InvalidHandle = 3;
    public const int NoMemory = 4;
    public const int BadOrMissingMode = 5;
    public const int InvalidLocalAddress = 6;
    public const int InvalidRemoteAddress = 7;
    public const int BadOrMissingFamily = 8;
    public const int DuplicateSocket = 9;
    public const int NoSuchPort = 10;
    public const int InvalidProtocol = 11;
    public const int BadParameter = 12;
    public const int NoBuffers = 13;
    public const int Unauthorised = 14;
    public const int NoRoute = 15;
    public const int OperationNotSupported = 16;

    /// <summary>
    /// Returned by <c>send</c> on a stream socket whose downlink isn't
    /// connected (e.g. SABM/UA hasn't completed, or the peer has
    /// disconnected). Not enumerated in PWP-0222 / PWP-0245 but real
    /// xrouter emits it as <c>errCode:17, errText:"Not connected"</c>.
    /// </summary>
    public const int NotConnected = 17;

    /// <summary>Canonical text for an error code (matching the spec).</summary>
    public static string Text(int code) => code switch
    {
        Ok => "Ok",
        Unspecified => "Unspecified",
        BadOrMissingType => "Bad or missing type",
        InvalidHandle => "Invalid handle",
        NoMemory => "No memory",
        BadOrMissingMode => "Bad or missing mode",
        InvalidLocalAddress => "Invalid local address",
        InvalidRemoteAddress => "Invalid remote address",
        BadOrMissingFamily => "Bad or missing family",
        DuplicateSocket => "Duplicate socket",
        NoSuchPort => "No such port",
        InvalidProtocol => "Invalid protocol",
        BadParameter => "Bad parameter",
        NoBuffers => "No buffers",
        Unauthorised => "Unauthorised",
        NoRoute => "No Route",
        OperationNotSupported => "Operation not supported",
        NotConnected => "Not connected",
        _ => $"Unknown ({code})",
    };

    /// <summary>True for transient errors that may succeed on retry.</summary>
    public static bool IsTransient(int code) =>
        code is Unspecified or NoMemory or NoBuffers;
}

/// <summary>
/// Wire-level RHP message type discriminators (the <c>type</c> field).
/// </summary>
public static class RhpMessageType
{
    public const string Auth = "auth";
    public const string AuthReply = "authReply";
    public const string Open = "open";
    public const string OpenReply = "openReply";
    public const string Socket = "socket";
    public const string SocketReply = "socketReply";
    public const string Bind = "bind";
    public const string BindReply = "bindReply";
    public const string Listen = "listen";
    public const string ListenReply = "listenReply";
    public const string Connect = "connect";
    public const string ConnectReply = "connectReply";
    public const string Send = "send";
    public const string SendReply = "sendReply";
    public const string SendTo = "sendto";
    public const string SendToReply = "sendtoReply";
    public const string Recv = "recv";
    public const string Accept = "accept";
    public const string Status = "status";
    public const string StatusReply = "statusReply";
    public const string Close = "close";
    public const string CloseReply = "closeReply";
}
