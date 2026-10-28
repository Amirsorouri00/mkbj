using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// The shared signature-based detector in BuildingBlocks. Everything here is content-only: the
/// detector never sees a file name or a client MIME type, which is exactly the property that
/// makes a renamed file unable to masquerade as another type.
/// </summary>
public class FileTypeDetectorTests
{
    private readonly FileTypeDetector _sut = new();

    public static TheoryData<string, FileType, string, string?> SupportedFormats => new()
    {
        { nameof(FileSamples.Pdf), FileType.Pdf, "application/pdf", ".pdf" },
        { nameof(FileSamples.Jpeg), FileType.Jpeg, "image/jpeg", ".jpg" },
        { nameof(FileSamples.Png), FileType.Png, "image/png", ".png" },
        { nameof(FileSamples.Gif87a), FileType.Gif, "image/gif", ".gif" },
        { nameof(FileSamples.Gif89a), FileType.Gif, "image/gif", ".gif" },
        { nameof(FileSamples.Bmp), FileType.Bmp, "image/bmp", ".bmp" },
        { nameof(FileSamples.Webp), FileType.Webp, "image/webp", ".webp" },
        { nameof(FileSamples.TiffLittleEndian), FileType.Tiff, "image/tiff", ".tiff" },
        { nameof(FileSamples.TiffBigEndian), FileType.Tiff, "image/tiff", ".tiff" },
        { nameof(FileSamples.PlainZip), FileType.Zip, "application/zip", ".zip" },
        { nameof(FileSamples.EmptyZip), FileType.Zip, "application/zip", ".zip" },
        { nameof(FileSamples.Docx), FileType.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx" },
        { nameof(FileSamples.Xlsx), FileType.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx" },
        { nameof(FileSamples.Pptx), FileType.Pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx" },
        { nameof(FileSamples.OleCompoundFile), FileType.OleCompoundFile, "application/x-ole-storage", null },
        { nameof(FileSamples.Gzip), FileType.Gzip, "application/gzip", ".gz" },
        { nameof(FileSamples.SevenZip), FileType.SevenZip, "application/x-7z-compressed", ".7z" },
        { nameof(FileSamples.Rar4), FileType.Rar, "application/vnd.rar", ".rar" },
        { nameof(FileSamples.Rar5), FileType.Rar, "application/vnd.rar", ".rar" },
    };

    public static TheoryData<string, FileType> SupportedSamples()
    {
        var data = new TheoryData<string, FileType>();
        foreach (var row in SupportedFormats)
            data.Add(row.Data.Item1, row.Data.Item2);
        return data;
    }

    private static byte[] Sample(string name) =>
        (byte[])typeof(FileSamples).GetProperty(name)!.GetValue(null)!;

    // ----- positive -----

    [Theory]
    [MemberData(nameof(SupportedFormats))]
    public void Detects_Every_Supported_Format_From_Its_Content(string sample, FileType expected, string mimeType, string? extension)
    {
        var result = _sut.Detect(Sample(sample));

        result.IsDetected.Should().BeTrue();
        result.FileType.Should().Be(expected);
        result.MimeType.Should().Be(mimeType);
        result.Extension.Should().Be(extension);
    }

    [Fact]
    public void Every_Detectable_Type_Has_A_Mime_Type()
    {
        foreach (var type in Enum.GetValues<FileType>().Where(t => t != FileType.Unknown))
            FileTypeMetadata.GetMimeType(type).Should().NotBeNullOrEmpty(because: $"{type} must map to a MIME type");
    }

    [Fact]
    public void Content_Longer_Than_The_Header_Length_Is_Accepted_And_Ignored_Past_The_Header()
    {
        var content = new byte[_sut.HeaderLength * 4];
        FileSamples.Png.CopyTo(content, 0);

        _sut.Detect(content).FileType.Should().Be(FileType.Png);
    }

    // ----- negative -----

    [Fact]
    public void Empty_Content_Is_Unknown()
    {
        _sut.Detect(ReadOnlySpan<byte>.Empty).Should().Be(FileTypeDetectionResult.Unknown);
        _sut.Detect([]).IsDetected.Should().BeFalse();
    }

    [Fact]
    public void Unknown_Result_Carries_No_Mime_Type_Or_Extension()
    {
        var result = FileTypeDetectionResult.Unknown;

        result.FileType.Should().Be(FileType.Unknown);
        result.MimeType.Should().BeNull();
        result.Extension.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(SupportedSamples))]
    public void A_Truncated_Header_Never_Throws_And_Is_Never_Misclassified(string sample, FileType expected)
    {
        var bytes = Sample(sample);

        for (var length = 0; length < bytes.Length; length++)
        {
            var result = _sut.Detect(bytes.AsSpan(0, length));

            // A prefix may legitimately be recognised (e.g. a ZIP cut before its Office parts),
            // but only as the same type or its generic container — never as something unrelated.
            if (result.IsDetected && result.FileType != expected)
                result.FileType.Should().Be(FileType.Zip, because: $"{sample} truncated to {length} bytes");
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44 })]             // "%PD"
    [InlineData(new byte[] { 0xFF, 0xD8 })]                   // JPEG SOI without the marker byte
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A })] // PNG minus one byte
    public void An_Insufficient_Header_Is_Unknown(byte[] header) =>
        _sut.Detect(header).IsDetected.Should().BeFalse();

    [Theory]
    [InlineData("plain text content")]
    [InlineData("this is not really a pdf")]
    [InlineData("{\"json\": true}")]
    [InlineData("<?xml version=\"1.0\"?><root/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    public void Text_Content_Is_Unknown(string text) =>
        _sut.Detect(Encoding.UTF8.GetBytes(text)).IsDetected.Should().BeFalse();

    [Fact]
    public void A_Riff_Container_That_Is_Not_Webp_Is_Unknown() =>
        _sut.Detect(FileSamples.Wav).IsDetected.Should().BeFalse();

    [Fact]
    public void Two_Ascii_Bytes_BM_Alone_Are_Not_A_Bitmap()
    {
        _sut.Detect("BM this is a text note"u8).IsDetected.Should().BeFalse();

        var bmpWithBogusDibHeader = FileSamples.Bmp;
        BinaryPrimitives.WriteUInt32LittleEndian(bmpWithBogusDibHeader.AsSpan(14), 41);
        _sut.Detect(bmpWithBogusDibHeader).IsDetected.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_Binary_Data_Is_Unknown(int seed)
    {
        var bytes = FileSamples.RandomBytes(4096, seed);
        // Make sure the random prefix cannot accidentally start with a real signature.
        bytes[0] = 0x00;

        _sut.Detect(bytes).IsDetected.Should().BeFalse();
    }

    // ----- security -----

    [Fact]
    public void An_Executable_Is_Unknown_Whatever_It_Is_Called()
    {
        // "malicious.exe" renamed to "document.pdf": the detector only ever sees the bytes.
        _sut.Detect(FileSamples.Executable).Should().Be(FileTypeDetectionResult.Unknown);
    }

    [Fact]
    public void A_Pdf_Signature_Is_Only_Recognised_At_Offset_Zero() =>
        _sut.Detect([0x00, .. FileSamples.Pdf]).IsDetected.Should().BeFalse();

    // ----- ZIP-based containers -----

    [Fact]
    public void A_Zip_With_An_Office_Folder_But_No_Package_Marker_Stays_A_Zip() =>
        _sut.Detect(FileSamples.Zip("word/document.xml", "word/styles.xml")).FileType.Should().Be(FileType.Zip);

    [Fact]
    public void A_Zip_With_Only_A_Package_Marker_Stays_A_Zip() =>
        _sut.Detect(FileSamples.Zip("[Content_Types].xml", "_rels/.rels", "data/payload.bin")).FileType.Should().Be(FileType.Zip);

    [Fact]
    public void An_Office_Folder_Name_Only_As_A_Substring_Does_Not_Count() =>
        _sut.Detect(FileSamples.Zip("[Content_Types].xml", "backup/word/document.xml")).FileType.Should().Be(FileType.Zip);

    [Fact]
    public void Package_Entries_Are_Found_Regardless_Of_Order() =>
        _sut.Detect(FileSamples.Zip("xl/workbook.xml", "docProps/app.xml", "[Content_Types].xml")).FileType.Should().Be(FileType.Xlsx);

    [Fact]
    public void A_Package_Cut_Before_Its_Main_Part_Falls_Back_To_Zip()
    {
        var docx = FileSamples.Docx;
        var firstMainPart = IndexOf(docx, "word/"u8);

        _sut.Detect(docx.AsSpan(0, firstMainPart)).FileType.Should().Be(FileType.Zip);
    }

    [Fact]
    public void Office_Parts_Beyond_The_Header_Window_Are_Not_Guessed()
    {
        // A first entry larger than the whole window pushes every later entry out of reach.
        var rest = FileSamples.Zip("_rels/.rels", "word/document.xml");
        var withLargeFirstEntry = LargeStoredEntryFirst(_sut.HeaderLength * 2, rest);

        _sut.Detect(withLargeFirstEntry).FileType.Should().Be(FileType.Zip);
        // Control: the same entries within reach are recognised.
        _sut.Detect(LargeStoredEntryFirst(16, rest)).FileType.Should().Be(FileType.Docx);
    }

    [Fact]
    public void A_Malformed_Local_Header_Does_Not_Throw()
    {
        var docx = FileSamples.Docx;
        // Claim a file-name length far larger than the buffer.
        BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(26), ushort.MaxValue);
        // And a compressed size that would overflow a naive int offset.
        BinaryPrimitives.WriteUInt32LittleEndian(docx.AsSpan(18), uint.MaxValue - 1);

        _sut.Detect(docx).FileType.Should().Be(FileType.Zip);
    }

    [Fact]
    public void A_Local_Header_With_Deferred_Sizes_Stops_The_Walk_Safely()
    {
        var docx = FileSamples.Docx;
        // General-purpose flag bit 3: sizes live in a trailing data descriptor.
        BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(6), 0x0008);

        _sut.Detect(docx).FileType.Should().Be(FileType.Zip);
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);

    /// <summary>A stored (uncompressed) local entry of <paramref name="size"/> bytes, then the rest of a ZIP.</summary>
    private static byte[] LargeStoredEntryFirst(int size, byte[] rest)
    {
        var name = "big.bin"u8;
        var header = new byte[30 + name.Length];
        "PK\x03\x04"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)name.Length);
        name.CopyTo(header.AsSpan(30));
        return [.. header, .. new byte[size], .. rest];
    }
}
