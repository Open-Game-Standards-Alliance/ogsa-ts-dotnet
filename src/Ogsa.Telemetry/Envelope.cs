using System;
using System.Collections.Generic;

namespace Ogsa.Telemetry
{

/// <summary>Frame type byte in the OMT1 envelope (see implementation-udp.md).</summary>
public enum FrameType : byte
{
    Discovery = 0x00,
    Motion = 0x01,
    Event = 0x02,
}

/// <summary>
/// The 8-byte OMT1 envelope plus Cap'n Proto message (de)serialization helpers.
/// Layout: bytes 0-3 magic "OMT1", byte 4 frame type, bytes 5-7 reserved
/// (send 0, ignore on receive), byte 8+ the Cap'n Proto message in standard
/// segment-table encoding (word-aligned, readable in place).
/// </summary>
public static class Envelope
{
    public const int HeaderSize = 8;
    static readonly byte[] Magic = { (byte)'O', (byte)'M', (byte)'T', (byte)'1' };

    public static byte[] Wrap(FrameType type, ReadOnlySpan<byte> capnpMessage)
    {
        var d = new byte[HeaderSize + capnpMessage.Length];
        Magic.AsSpan().CopyTo(d);
        d[4] = (byte)type;
        capnpMessage.CopyTo(d.AsSpan(HeaderSize));
        return d;
    }

    /// <summary>Checks magic; frame types are advisory — unknown types yield
    /// false (receivers silently skip foreign/future frames).</summary>
    public static bool TryUnwrap(ReadOnlySpan<byte> datagram, out FrameType type, out ReadOnlyMemory<byte> capnpMessage)
    {
        type = default;
        capnpMessage = default;
        if (datagram.Length < HeaderSize) return false;
        if (!datagram.Slice(0, 4).SequenceEqual(Magic)) return false;
        type = (FrameType)datagram[4];
        // netstandard2.1-safe overload (generic Enum.IsDefined<T> is .NET 5+);
        // boxing is fine here — receive path, once per datagram
        if (!Enum.IsDefined(typeof(FrameType), type)) return false;
        capnpMessage = new ReadOnlyMemory<byte>(datagram.ToArray(), HeaderSize, datagram.Length - HeaderSize);
        return true;
    }

    /// <summary>Encodes a Cap'n Proto message in standard segment-table
    /// format (u32 count-1, u32 sizes, pad to word boundary, segments).</summary>
    public static byte[] EncodeMessage(IReadOnlyList<Memory<ulong>> segments)
    {
        int n = segments.Count;
        long words = 0;
        for (int i = 0; i < n; i++) words += segments[i].Length;
        int tableWords = 1 + n + ((1 + n) & 1);
        var buf = new byte[(tableWords + words) * 8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), (uint)(n - 1));
        for (int i = 0; i < n; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                buf.AsSpan(4 * (1 + i)), (uint)segments[i].Length);
        }
        long off = tableWords;
        for (int i = 0; i < n; i++)
        {
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(segments[i].Span)
                .CopyTo(buf.AsSpan((int)off * 8));
            off += segments[i].Length;
        }
        return buf;
    }

    public static byte[] EncodeMessage(Capnp.MessageBuilder mb) => EncodeMessage(mb.Frame.Segments);

    /// <summary>Decodes a segment-table-encoded message into a WireFrame.</summary>
    public static Capnp.WireFrame DecodeMessage(ReadOnlySpan<byte> message)
    {
        if (message.Length < 8) throw new Capnp.DeserializationException("message too short");
        uint n1 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(0, 4));
        int n = (int)n1 + 1;
        if (n < 1 || n > 512) throw new Capnp.DeserializationException("implausible segment count");
        int tableWords = 1 + n + ((1 + n) & 1);
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
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(
                message.Slice((int)off * 8, (int)len * 8)).CopyTo(seg);
            segments[i] = seg;
            off += len;
        }
        return new Capnp.WireFrame(segments);
    }
}
}
