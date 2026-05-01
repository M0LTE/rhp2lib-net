using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;

namespace RhpV2.Client;

/// <summary>
/// Asynchronous RHPv2 client.  Connects to an XRouter (or compatible) node
/// over TCP, exchanges length-prefixed JSON frames, correlates request/reply
/// messages by <c>id</c>, and surfaces asynchronous notifications (RECV,
/// ACCEPT, STATUS, server-initiated CLOSE) via events.
///
/// Default port per spec is 9000.
/// </summary>
public sealed class RhpClient : IAsyncDisposable, IDisposable
{
    /// <summary>The default RHPv2 TCP port (per PWP-0222).</summary>
    public const int DefaultPort = 9000;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly TcpClient? _tcpClient;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<RhpMessage>> _pending = new();
    private int _nextId;
    private readonly CancellationTokenSource _readerCts = new();
    private Task? _readerTask;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disposed;

    /// <summary>Raised when an asynchronous RECV message arrives.</summary>
    public event EventHandler<RhpReceivedEventArgs>? Received;
    /// <summary>Raised when an ACCEPT message arrives on a passive listener.</summary>
    public event EventHandler<RhpAcceptedEventArgs>? Accepted;
    /// <summary>Raised when a server-initiated STATUS update arrives.</summary>
    public event EventHandler<RhpStatusEventArgs>? StatusChanged;
    /// <summary>Raised when the server tells us a downlink has closed.</summary>
    public event EventHandler<RhpClosedEventArgs>? Closed;
    /// <summary>Raised on unrecognised / forward-compatible frames.</summary>
    public event EventHandler<RhpUnknownEventArgs>? UnknownReceived;
    /// <summary>Raised when the read loop terminates (clean or with error).</summary>
    public event EventHandler<Exception?>? Disconnected;

    private RhpClient(Stream stream, bool ownsStream, TcpClient? tcpClient)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _tcpClient = tcpClient;
    }

    /// <summary>Wrap an already-connected stream (useful for testing).</summary>
    public static RhpClient FromStream(Stream stream, bool ownsStream = false)
    {
        var client = new RhpClient(stream, ownsStream, tcpClient: null);
        client.Start();
        return client;
    }

    /// <summary>Open a TCP connection to an RHPv2 node.</summary>
    public static async Task<RhpClient> ConnectAsync(
        string host,
        int port = DefaultPort,
        CancellationToken ct = default)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
            var client = new RhpClient(tcp.GetStream(), ownsStream: true, tcpClient: tcp);
            client.Start();
            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private void Start()
    {
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token));
    }

    /// <summary>True until the underlying stream is disposed.</summary>
    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0
        && (_tcpClient is null || _tcpClient.Connected);

    private int NextRequestId()
    {
        // Avoid 0 — many transports treat it as "unset".  We never wrap to 0.
        while (true)
        {
            var v = Interlocked.Increment(ref _nextId);
            if (v != 0) return v;
        }
    }

    /// <summary>
    /// Send a request and await its reply.  The reply is matched on the
    /// auto-assigned <c>id</c> field.  If the reply contains a non-zero error
    /// code, throws <see cref="RhpServerException"/>.
    /// </summary>
    public async Task<TReply> RequestAsync<TReply>(
        RhpMessage request,
        CancellationToken ct = default)
        where TReply : RhpMessage
    {
        if (request.Id is null) request.Id = NextRequestId();
        var tcs = new TaskCompletionSource<RhpMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id.Value, tcs))
            throw new InvalidOperationException(
                $"Duplicate request id {request.Id.Value}.");

        try
        {
            await SendAsync(request, ct).ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var reply = await tcs.Task.ConfigureAwait(false);

            if (reply is TReply typed)
            {
                ThrowIfErrorReply(typed);
                return typed;
            }
            throw new RhpProtocolException(
                $"Expected reply of type {typeof(TReply).Name} but got {reply.GetType().Name} (type='{reply.Type}').");
        }
        finally
        {
            _pending.TryRemove(request.Id.Value, out _);
        }
    }

    private static void ThrowIfErrorReply(RhpMessage reply)
    {
        switch (reply)
        {
            case AuthReplyMessage a when a.ErrCode != 0:
                throw new RhpServerException(a.ErrCode, a.ErrText);
            case OpenReplyMessage o when o.ErrCode != 0:
                throw new RhpServerException(o.ErrCode, o.ErrText);
            case SocketReplyMessage s when s.ErrCode != 0:
                throw new RhpServerException(s.ErrCode, s.ErrText);
            case BindReplyMessage b when b.ErrCode != 0:
                throw new RhpServerException(b.ErrCode, b.ErrText);
            case ListenReplyMessage l when l.ErrCode != 0:
                throw new RhpServerException(l.ErrCode, l.ErrText);
            case ConnectReplyMessage c when c.ErrCode != 0:
                throw new RhpServerException(c.ErrCode, c.ErrText);
            case SendReplyMessage sr when sr.ErrCode != 0:
                throw new RhpServerException(sr.ErrCode, sr.ErrText);
            case SendToReplyMessage sto when sto.ErrCode != 0:
                throw new RhpServerException(sto.ErrCode, sto.ErrText);
            case StatusReplyMessage st when st.ErrCode != 0:
                throw new RhpServerException(st.ErrCode, st.ErrText);
            case CloseReplyMessage cl when cl.ErrCode != 0:
                throw new RhpServerException(cl.ErrCode, cl.ErrText);
        }
    }

    /// <summary>
    /// Send a fire-and-forget message (no reply correlation).  When the
    /// message has no <c>id</c> the spec says the server only replies on
    /// error — those errors are surfaced through events.
    /// </summary>
    public async Task SendAsync(RhpMessage message, CancellationToken ct = default)
    {
        var bytes = RhpJson.Serialize(message);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RhpFraming.WriteFrameAsync(_stream, bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Exception? terminator = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await RhpFraming.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                if (frame is null) break; // clean EOS
                RhpMessage msg;
                try
                {
                    msg = RhpJson.Deserialize(frame);
                }
                catch (Exception ex)
                {
                    UnknownReceived?.Invoke(this, new RhpUnknownEventArgs(
                        new UnknownMessage("<parse-error>", new System.Text.Json.Nodes.JsonObject())));
                    System.Diagnostics.Debug.WriteLine($"RHP parse error: {ex.Message}");
                    continue;
                }

                Dispatch(msg);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            terminator = ex;
        }
        finally
        {
            // Fault any in-flight requests so callers don't hang forever.
            foreach (var kvp in _pending)
                kvp.Value.TrySetException(
                    terminator is null
                        ? new RhpTransportException("RHP connection closed.")
                        : new RhpTransportException("RHP connection failed.", terminator));
            Disconnected?.Invoke(this, terminator);
        }
    }

    private void Dispatch(RhpMessage msg)
    {
        // Reply correlation: any reply with an id we know about goes there.
        if (msg.Id is int rid && _pending.TryGetValue(rid, out var tcs))
        {
            tcs.TrySetResult(msg);
            return;
        }

        switch (msg)
        {
            case RecvMessage recv:
                Received?.Invoke(this, new RhpReceivedEventArgs(recv));
                break;
            case AcceptMessage acc:
                Accepted?.Invoke(this, new RhpAcceptedEventArgs(acc));
                break;
            case StatusMessage stat:
                StatusChanged?.Invoke(this, new RhpStatusEventArgs(stat));
                break;
            case CloseMessage cls:
                Closed?.Invoke(this, new RhpClosedEventArgs(cls.Handle));
                break;
            default:
                UnknownReceived?.Invoke(this, new RhpUnknownEventArgs(msg));
                break;
        }
    }

    // -----------------------------------------------------------------
    //  Convenience: high-level operations
    // -----------------------------------------------------------------

    public Task AuthenticateAsync(string user, string pass, CancellationToken ct = default)
        => RequestAsync<AuthReplyMessage>(new AuthMessage { User = user, Pass = pass }, ct);

    public async Task<int> OpenAsync(
        string family, string mode,
        string? port = null, string? local = null, string? remote = null,
        OpenFlags flags = OpenFlags.Passive,
        CancellationToken ct = default)
    {
        var reply = await RequestAsync<OpenReplyMessage>(new OpenMessage
        {
            Pfam = family, Mode = mode,
            Port = port, Local = local, Remote = remote,
            Flags = (int)flags,
        }, ct).ConfigureAwait(false);
        return reply.Handle;
    }

    public async Task<int> SocketAsync(string family, string mode, CancellationToken ct = default)
    {
        var reply = await RequestAsync<SocketReplyMessage>(new SocketMessage
        {
            Pfam = family, Mode = mode,
        }, ct).ConfigureAwait(false);
        return reply.Handle ?? throw new RhpProtocolException("socketReply missing handle.");
    }

    public Task BindAsync(int handle, string local, string? port = null, CancellationToken ct = default)
        => RequestAsync<BindReplyMessage>(new BindMessage { Handle = handle, Local = local, Port = port }, ct);

    public Task ListenAsync(int handle, OpenFlags flags = OpenFlags.Passive, CancellationToken ct = default)
        => RequestAsync<ListenReplyMessage>(new ListenMessage { Handle = handle, Flags = (int)flags }, ct);

    public Task ConnectAsync(int handle, string remote, CancellationToken ct = default)
        => RequestAsync<ConnectReplyMessage>(new ConnectMessage { Handle = handle, Remote = remote }, ct);

    public Task<SendReplyMessage> SendOnHandleAsync(int handle, string data, CancellationToken ct = default)
        => RequestAsync<SendReplyMessage>(new SendMessage { Handle = handle, Data = data }, ct);

    public Task<SendReplyMessage> SendOnHandleAsync(int handle, ReadOnlySpan<byte> data, CancellationToken ct = default)
        => SendOnHandleAsync(handle, RhpDataEncoding.ToWireString(data), ct);

    public Task<SendToReplyMessage> SendToAsync(
        int handle, string data,
        string? port = null, string? local = null, string? remote = null, int? tos = null,
        CancellationToken ct = default)
        => RequestAsync<SendToReplyMessage>(new SendToMessage
        {
            Handle = handle, Data = data, Port = port, Local = local, Remote = remote, Tos = tos,
        }, ct);

    public Task CloseAsync(int handle, CancellationToken ct = default)
        => RequestAsync<CloseReplyMessage>(new CloseMessage { Handle = handle }, ct);

    public Task<StatusReplyMessage> QueryStatusAsync(int handle, CancellationToken ct = default)
        => RequestAsync<StatusReplyMessage>(new StatusMessage { Handle = handle }, ct);

    // -----------------------------------------------------------------
    //  Disposal
    // -----------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _readerCts.Cancel(); } catch { }
        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch { /* swallow */ }
        }
        if (_ownsStream) await _stream.DisposeAsync().ConfigureAwait(false);
        _tcpClient?.Dispose();
        _readerCts.Dispose();
        _writeLock.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
