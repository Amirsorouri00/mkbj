using System.Buffers.Binary;
using EwaysVNext.Core.Application.FileTypeDetection;

namespace EwaysVNext.Core.Implementation.FileTypeDetection;

public sealed class FileTypeDetector : IFileTypeDetector
{
    public const int DefaultHeaderLength = 8 * 1024;

    private static readonly FileSignature[] Signatures =
    [
        // Offset 0 only, as before the move to BuildingBlocks (the spec tolerates a later header,
        // but accepting one would let arbitrary leading bytes through).
        new(FileType.Pdf, "%PDF"u8),
        new(FileType.Jpeg, [0xFF, 0xD8, 0xFF]),
        new(FileType.Png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        new(FileType.Gif, "GIF87a"u8),
        new(FileType.Gif, "GIF89a"u8),
        new(FileType.Bmp, "BM"u8, IsBitmap),
        new(FileType.Webp, [(0, "RIFF"u8.ToArray()), (8, "WEBP"u8.ToArray())]),
        new(FileType.Tiff, "II*\0"u8),
        new(FileType.Tiff, "MM\0*"u8),
        new(FileType.Zip, "PK\x03\x04"u8, OfficeOpenXmlInspector.Classify),
        new(FileType.Zip, "PK\x05\x06"u8), 
        new(FileType.OleCompoundFile, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
        new(FileType.Gzip, [0x1F, 0x8B, 0x08]), 
        new(FileType.SevenZip, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),
        new(FileType.Rar, "Rar!\x1A\x07\x00"u8),    
        new(FileType.Rar, "Rar!\x1A\x07\x01\x00"u8),
    ];

    public int HeaderLength => DefaultHeaderLength;

    public FileTypeDetectionResult Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length > DefaultHeaderLength)
            content = content[..DefaultHeaderLength];

        foreach (var signature in Signatures)
        {
            var fileType = signature.Match(content);
            if (fileType != FileType.Unknown)
                return new FileTypeDetectionResult(fileType);
        }

        return FileTypeDetectionResult.Unknown;
    }

    private static FileType IsBitmap(ReadOnlySpan<byte> content)
    {
        if (content.Length < 18)
            return FileType.Unknown;

        return BinaryPrimitives.ReadUInt32LittleEndian(content[14..]) switch
        {
            12 or 40 or 52 or 56 or 64 or 108 or 124 => FileType.Bmp,
            _ => FileType.Unknown,
        };
    }
}
