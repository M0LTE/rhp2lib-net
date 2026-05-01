namespace RhpV2.Client;

/// <summary>Base exception type for protocol-level RHPv2 errors.</summary>
public class RhpProtocolException : Exception
{
    public RhpProtocolException(string message) : base(message) { }
    public RhpProtocolException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when an RHP request returns a non-zero error code.  The numeric
/// code is the <c>errcode</c>/<c>errCode</c> field from the reply.
/// </summary>
public class RhpServerException : RhpProtocolException
{
    public int ErrorCode { get; }
    public string? ErrorText { get; }

    public RhpServerException(int code, string? text)
        : base($"RHP error {code}: {text ?? Protocol.RhpErrorCode.Text(code)}")
    {
        ErrorCode = code;
        ErrorText = text;
    }
}

/// <summary>Thrown when the underlying transport closes mid-conversation.</summary>
public class RhpTransportException : RhpProtocolException
{
    public RhpTransportException(string message) : base(message) { }
    public RhpTransportException(string message, Exception inner) : base(message, inner) { }
}
