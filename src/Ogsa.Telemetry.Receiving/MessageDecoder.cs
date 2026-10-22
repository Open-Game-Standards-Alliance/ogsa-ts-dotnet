#nullable enable
using System;

namespace Ogsa.Telemetry
{

/// <summary>Decodes the Cap'n Proto payload of an OMT1 datagram (after
/// <see cref="Envelope.TryUnwrap"/>) for the generated CapnpGen readers.
/// Accepts any valid framing: single- or multi-segment.</summary>
public static class MessageDecoder
{
    /// <summary>Decodes a segment-table-encoded message into a WireFrame.</summary>
    public static Capnp.WireFrame DecodeMessage(ReadOnlySpan<byte> message)
    {
        if (message.Length < 8) throw new Capnp.DeserializationException("message too short");
        uint n1 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(0, 4));
        int n = (int)n1 + 1;
        if (n < 1 || n > 512) throw new Capnp.DeserializationException("implausible segment count");
        int tableWords = (1 + n + ((1 + n) & 1)) / 2; // canonical: even u32 count
        if (message.Length < tableWords * 8) throw new Capnp.DeserializationException("truncated segment table");
        long words = 0;
        for (int i = 0; i < n; i++)
        {
            words += System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(4 * (1 + i), 4));
        }
        if (message.Length < (tableWords + words) * 8)
            throw new Capnp.DeserializationException("truncated segments");

        var segments = new Memory<ulong>[n];
        long off = tableWords;
        for (int i = 0; i < n; i++)
        {
            uint len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(4 * (1 + i), 4));
            var seg = new ulong[len];
            for (int w = 0; w < len; w++)
                seg[w] = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                    message.Slice((int)(off + w) * 8, 8));
            segments[i] = seg;
            off += len;
        }
        return new Capnp.WireFrame(segments);
    }

    /// <summary>The message root, ready for <c>CapnpGen.*.READER.create</c>.</summary>
    public static Capnp.DeserializerState DecodeRoot(ReadOnlySpan<byte> message) =>
        Capnp.DeserializerState.CreateRoot(DecodeMessage(message));
}
}
