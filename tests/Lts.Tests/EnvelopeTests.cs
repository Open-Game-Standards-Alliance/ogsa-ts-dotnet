using Lts;
using Xunit;

public class EnvelopeTests
{
    static byte[] Msg(int len) => Enumerable.Range(0, len).Select(i => (byte)i).ToArray();

    [Fact]
    public void Wrap_Unwrap_RoundTrips()
    {
        var datagram = Envelope.Wrap(FrameType.Motion, Msg(64));
        Assert.True(Envelope.TryUnwrap(datagram, out var type, out var payload));
        Assert.Equal(FrameType.Motion, type);
        Assert.Equal(64, payload.Length);
        Assert.Equal(0x00, payload.Span[0]);
        Assert.Equal(0x3F, payload.Span[63]);
    }

    [Fact]
    public void Foreign_Magic_Is_Skipped()
    {
        var d = Envelope.Wrap(FrameType.Discovery, Msg(8));
        d[0] = (byte)'X';
        Assert.False(Envelope.TryUnwrap(d, out _, out _));
    }

    [Fact]
    public void Unknown_FrameType_Is_Skipped()
    {
        var d = Envelope.Wrap(FrameType.Discovery, Msg(8));
        d[4] = 0x7F;
        Assert.False(Envelope.TryUnwrap(d, out _, out _));
    }

    [Fact]
    public void Reserved_Bytes_Nonzero_Is_Tolerated()
    {
        var d = Envelope.Wrap(FrameType.Discovery, Msg(8));
        d[5] = 0xAB; d[6] = 0xCD; d[7] = 0xEF;
        Assert.True(Envelope.TryUnwrap(d, out var type, out _));
        Assert.Equal(FrameType.Discovery, type);
    }

    [Fact]
    public void Short_Datagram_Is_Skipped()
    {
        Assert.False(Envelope.TryUnwrap(new byte[7], out _, out _));
        Assert.False(Envelope.TryUnwrap(Array.Empty<byte>(), out _, out _));
    }
}
