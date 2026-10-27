using Ogs.Telemetry;
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

    [Fact]
    public void EncodeDatagram_Matches_Wrap_Of_EncodeMessage()
    {
        ulong[] words = { 0x0000_0001_0000_0000, 0x3FF0_0000_0000_0000 };
        var combined = Envelope.EncodeDatagram(FrameType.Event, words);
        var separate = Envelope.Wrap(FrameType.Event, Envelope.EncodeMessage(words));
        Assert.Equal(separate, combined);
    }

    [Fact]
    public void EncodeMessage_Writes_One_Word_Table_And_Little_Endian_Words()
    {
        var bytes = Envelope.EncodeMessage(new ulong[] { 0x0102_0304_0506_0708 });
        Assert.Equal(new byte[]
        {
            0, 0, 0, 0, 1, 0, 0, 0,             // segment count − 1 = 0, size 1 word
            8, 7, 6, 5, 4, 3, 2, 1,             // the word, least significant byte first
        }, bytes);
    }
}
