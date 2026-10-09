#nullable enable
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
    /// false (receivers silently skip foreign/future frames). Takes a
    /// Memory so the payload slice is zero-copy when the caller owns the
    /// buffer (byte[] converts implicitly).</summary>
    public static bool TryUnwrap(ReadOnlyMemory<byte> datagram, out FrameType type, out ReadOnlyMemory<byte> capnpMessage)
    {
        type = default;
        capnpMessage = default;
        if (datagram.Length < HeaderSize) return false;
        var span = datagram.Span;
        if (!span.Slice(0, 4).SequenceEqual(Magic)) return false;
        var t = (FrameType)span[4];
        // values are contiguous 0..Event — a range check avoids
        // Enum.IsDefined's per-datagram boxing and is netstandard2.1-safe
        if (t > FrameType.Event) return false;
        type = t;
        capnpMessage = datagram.Slice(HeaderSize);
        return true;
    }

    /// <summary>Encodes a Cap'n Proto message in standard segment-table
    /// format. Table = u32 (count-1) + N x u32 sizes, padded to an even
    /// u32 count (so the table is a whole number of words, NOT a minimum
    /// of two words — a 1-segment table is exactly 1 word, per canonical
    /// capnproto; the old 2-word form made canonical readers see a NULL
    /// root). Segments follow word-aligned.</summary>
    public static byte[] EncodeMessage(IReadOnlyList<Memory<ulong>> segments)
    {
        int n = segments.Count;
        long words = 0;
        for (int i = 0; i < n; i++) words += segments[i].Length;
        int tableWords = (1 + n + ((1 + n) & 1)) / 2;
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

    /// <summary>Full datagram in one allocation and one copy: the 8-byte
    /// OMT1 header, then the segment-table encoding. Byte-identical to
    /// <c>Wrap(type, EncodeMessage(mb))</c> — the sender hot path uses this
    /// to avoid the extra buffer.</summary>
    public static byte[] EncodeDatagram(FrameType type, IReadOnlyList<Memory<ulong>> segments)
    {
        int n = segments.Count;
        long words = 0;
        for (int i = 0; i < n; i++) words += segments[i].Length;
        int tableWords = (1 + n + ((1 + n) & 1)) / 2;
        var buf = new byte[HeaderSize + (tableWords + words) * 8];
        Magic.AsSpan().CopyTo(buf);
        buf[4] = (byte)type;
        int off = HeaderSize;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            buf.AsSpan(off), (uint)(n - 1));
        for (int i = 0; i < n; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                buf.AsSpan(off + 4 * (1 + i)), (uint)segments[i].Length);
        }
        long w = tableWords;
        for (int i = 0; i < n; i++)
        {
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(segments[i].Span)
                .CopyTo(buf.AsSpan(HeaderSize + (int)w * 8));
            w += segments[i].Length;
        }
        return buf;
    }

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
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(
                message.Slice((int)off * 8, (int)len * 8)).CopyTo(seg);
            segments[i] = seg;
            off += len;
        }
        return new Capnp.WireFrame(segments);
    }
}
}
