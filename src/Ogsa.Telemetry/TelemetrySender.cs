using System.Net;
using System.Net.Sockets;

namespace Ogsa.Telemetry;

/// <summary>
/// LTS UDP-multicast sender. Default endpoint: 239.255.17.17:40123, TTL 1
/// (same subnet), loopback enabled for local development (see
/// implementation-udp.md).
/// </summary>
public sealed class TelemetrySender : IDisposable
{
    readonly Socket _socket;
    readonly IPEndPoint _endpoint;

    public TelemetrySender(string group = "239.255.17.17", int port = 40123,
                     int ttl = 1, bool loopback = true)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);
        if (loopback)
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
        _endpoint = new IPEndPoint(IPAddress.Parse(group), port);
    }

    public void Send(FrameType type, Capnp.MessageBuilder message) =>
        _socket.SendTo(Envelope.Wrap(type, Envelope.EncodeMessage(message)), _endpoint);

    public void Dispose() => _socket.Dispose();
}
