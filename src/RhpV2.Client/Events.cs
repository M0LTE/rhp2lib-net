using RhpV2.Client.Protocol;

namespace RhpV2.Client;

public sealed class RhpReceivedEventArgs : EventArgs
{
    public RecvMessage Message { get; }
    public RhpReceivedEventArgs(RecvMessage m) { Message = m; }
}

public sealed class RhpAcceptedEventArgs : EventArgs
{
    public AcceptMessage Message { get; }
    public RhpAcceptedEventArgs(AcceptMessage m) { Message = m; }
}

public sealed class RhpStatusEventArgs : EventArgs
{
    public StatusMessage Message { get; }
    public RhpStatusEventArgs(StatusMessage m) { Message = m; }
}

public sealed class RhpClosedEventArgs : EventArgs
{
    public int Handle { get; }
    public RhpClosedEventArgs(int handle) { Handle = handle; }
}

public sealed class RhpUnknownEventArgs : EventArgs
{
    public RhpMessage Message { get; }
    public RhpUnknownEventArgs(RhpMessage m) { Message = m; }
}
