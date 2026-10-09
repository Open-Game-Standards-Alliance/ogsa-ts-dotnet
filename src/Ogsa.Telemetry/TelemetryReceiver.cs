using System.Net;
using System.Net.Sockets;

namespace Ogsa.Telemetry;

/// <summary>
/// LTS UDP-multicast receiver. Joins the group and demuxes by frame type.
/// Foreign packets (bad magic) and unknown frame types are skipped silently
/// per the evolution rules; decode failures never kill the receive loop.
/// </summary>
public sealed class TelemetryReceiver : IDisposable
{
    readonly Socket _socket;
    readonly CancellationTokenSource _cts = new();
    Task? _loop;

    public event Action<CapnpGen.DiscoveryFrame.READER>? DiscoveryReceived;
    public event Action<CapnpGen.MotionFrame.READER>? MotionReceived;
    public event Action<CapnpGen.EventFrame.READER>? EventReceived;

    public long ForeignSkipped { get; private set; }
    public long DecodeErrors { get; private set; }

    public TelemetryReceiver(string group = "239.255.17.17", int port = 40123)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
            new MulticastOption(IPAddress.Parse(group)));
    }

    public void Start() => _loop ??= Task.Run(() => ReceiveLoop(_cts.Token));

    void ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[65536];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int len;
                if (_socket.Available == 0)
                {
                    if (!_socket.Poll(200_000, SelectMode.SelectRead)) continue; // 200 ms
                }
                len = _socket.ReceiveFrom(buf, ref from);
                if (!Envelope.TryUnwrap(buf.AsSpan(0, len), out var type, out var payload))
                {
                    ForeignSkipped++;
                    continue;
                }
                switch (type)
                {
                    case FrameType.Discovery:
                        DiscoveryReceived?.Invoke(
                            CapnpGen.DiscoveryFrame.READER.create(DecodeRoot(payload)));
                        break;
                    case FrameType.Motion:
                        MotionReceived?.Invoke(
                            CapnpGen.MotionFrame.READER.create(DecodeRoot(payload)));
                        break;
                    case FrameType.Event:
                        EventReceived?.Invoke(
                            CapnpGen.EventFrame.READER.create(DecodeRoot(payload)));
                        break;
                }
            }
            catch (Capnp.DeserializationException)
            {
                DecodeErrors++;
            }
            catch (SocketException)
            {
                // transient; keep receiving
            }
        }
    }

    static Capnp.DeserializerState DecodeRoot(ReadOnlyMemory<byte> payload)
    {
        var frame = Envelope.DecodeMessage(payload.Span);
        return Capnp.DeserializerState.CreateRoot(frame);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _socket.Close(); } catch (SocketException) { }
        _cts.Dispose();
    }
}
