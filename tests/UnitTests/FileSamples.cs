using System.IO.Compression;
using System.Text;

namespace EwaysVNext.UnitTests;

/// <summary>Minimal but structurally genuine file contents for file-type detection tests.</summary>
internal static class FileSamples
{
    public static byte[] Pdf => Encoding.ASCII.GetBytes("%PDF-1.7\n%âã\n1 0 obj\n<< /Type /Catalog >>\nendobj\n");
    public static byte[] Jpeg => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    public static byte[] Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    public static byte[] Gif87a => Encoding.ASCII.GetBytes("GIF87a\x01\x00\x01\x00");
    public static byte[] Gif89a => Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00");

    /// <summary>14-byte file header + the 40-byte BITMAPINFOHEADER size field.</summary>
    public static byte[] Bmp => ["B"u8[0], "M"u8[0], 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 40, 0, 0, 0, 1, 0, 0, 0];

    public static byte[] Webp => Riff("WEBP");
    public static byte[] Wav => Riff("WAVE");
    public static byte[] TiffLittleEndian => [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00];
    public static byte[] TiffBigEndian => [0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08];
    public static byte[] OleCompoundFile => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0];
    public static byte[] SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];
    public static byte[] Rar4 => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00, 0xCF];
    public static byte[] Rar5 => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00, 0x33];

    /// <summary>Windows PE executable header ("MZ" DOS stub).</summary>
    public static byte[] Executable => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];

    public static byte[] Gzip
    {
        get
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
                gzip.Write("hello"u8);
            return output.ToArray();
        }
    }

    public static byte[] EmptyZip => Zip();
    public static byte[] PlainZip => Zip("readme.txt", "notes/todo.txt");

    // Entry order matches what Microsoft Office writes.
    public static byte[] Docx => Zip("[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/_rels/document.xml.rels");
    public static byte[] Xlsx => Zip("[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/worksheets/sheet1.xml");
    public static byte[] Pptx => Zip("[Content_Types].xml", "_rels/.rels", "ppt/presentation.xml", "ppt/slides/slide1.xml");

    /// <summary>Builds a real ZIP whose entries each contain a little compressible XML.</summary>
    public static byte[] Zip(params string[] entryNames)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><part name=\"{name}\"/>"));
            }
        }

        return output.ToArray();
    }

    public static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Riff(string form) =>
        [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. Encoding.ASCII.GetBytes(form), .. "VP8 "u8];
}
