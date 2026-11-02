using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace EwaysVNext.Host.Seeding;

/// <summary>
/// A solid-colour PNG, written by hand.
/// <para>
/// Written rather than shipped as a file because the seed needs a distinct image per product and
/// 160 checked-in binaries would be 160 things to review. Hand-encoded rather than drawn with an
/// imaging library because the only library that would work everywhere is another dependency for
/// four rectangles of colour.
/// </para>
/// </summary>
public static class PlaceholderPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Truecolour RGB, 8 bits a channel — what every browser renders without question.</summary>
    private const byte BitDepth = 8;

    private const byte ColourTypeRgb = 2;

    public static byte[] Create(int width, int height, byte r, byte g, byte b)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        using var png = new MemoryStream();
        png.Write(Signature);

        WriteChunk(png, "IHDR", Header(width, height));
        WriteChunk(png, "IDAT", Pixels(width, height, r, g, b));
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = BitDepth;
        header[9] = ColourTypeRgb;
        // Compression 0, filter 0, interlace 0 — the only values the format defines.
        return header;
    }

    /// <summary>
    /// Every row is a filter byte followed by its pixels. Filter 0 means "no filtering": a solid
    /// colour compresses to nothing whatever the filter, and leaving it off keeps this readable.
    /// </summary>
    private static byte[] Pixels(int width, int height, byte r, byte g, byte b)
    {
        var row = new byte[1 + (width * 3)];
        for (var x = 0; x < width; x++)
        {
            row[1 + (x * 3)] = r;
            row[2 + (x * 3)] = g;
            row[3 + (x * 3)] = b;
        }

        using var raw = new MemoryStream();

        // ZLibStream, not DeflateStream: PNG's IDAT carries a zlib wrapper, and a bare deflate
        // stream decodes as a corrupt file in every viewer.
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++) zlib.Write(row);
        }

        return raw.ToArray();
    }

    private static void WriteChunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);

        // The CRC covers the type and the data but not the length — a detail the format is easy
        // to get wrong on, and which produces a file that opens nowhere.
        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc.GetCurrentHashAsUInt32());
        png.Write(checksum);
    }
}
