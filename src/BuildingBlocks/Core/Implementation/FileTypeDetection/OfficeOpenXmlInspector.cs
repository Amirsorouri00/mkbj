using System.Buffers.Binary;
using EwaysVNext.Core.Application.FileTypeDetection;

namespace EwaysVNext.Core.Implementation.FileTypeDetection;

internal static class OfficeOpenXmlInspector
{
    private const int LocalFileHeaderLength = 30;
    private const int MaxEntries = 64;
    private const ushort DataDescriptorFlag = 0x0008;

    private static ReadOnlySpan<byte> LocalFileHeaderSignature => "PK\x03\x04"u8;
    private static ReadOnlySpan<byte> ContentTypesEntry => "[Content_Types].xml"u8;
    private static ReadOnlySpan<byte> RelationshipsFolder => "_rels/"u8;
    private static ReadOnlySpan<byte> WordFolder => "word/"u8;
    private static ReadOnlySpan<byte> ExcelFolder => "xl/"u8;
    private static ReadOnlySpan<byte> PowerPointFolder => "ppt/"u8;

    public static FileType Classify(ReadOnlySpan<byte> content)
    {
        var hasPackageMarker = false;
        var mainPart = FileType.Unknown;
        var offset = 0;

        for (var entry = 0; entry < MaxEntries; entry++)
        {
            if (content.Length - offset < LocalFileHeaderLength)
                break;

            var header = content.Slice(offset, LocalFileHeaderLength);
            if (!header.StartsWith(LocalFileHeaderSignature))
                break;

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[18..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);

            if (content.Length - offset - LocalFileHeaderLength < nameLength)
                break;

            var name = content.Slice(offset + LocalFileHeaderLength, nameLength);
            if (name.SequenceEqual(ContentTypesEntry) || name.StartsWith(RelationshipsFolder))
                hasPackageMarker = true;
            else if (mainPart == FileType.Unknown)
                mainPart = MainPartOf(name);

            if (hasPackageMarker && mainPart != FileType.Unknown)
                return mainPart;

            // The next entry cannot be located when its sizes are deferred or ZIP64-escaped.
            if ((flags & DataDescriptorFlag) != 0 || compressedSize == uint.MaxValue)
                break;

            var next = (long)offset + LocalFileHeaderLength + nameLength + extraLength + compressedSize;
            if (next > content.Length)
                break;

            offset = (int)next;
        }

        return FileType.Zip;
    }

    private static FileType MainPartOf(ReadOnlySpan<byte> entryName)
    {
        if (entryName.StartsWith(WordFolder))
            return FileType.Docx;
        if (entryName.StartsWith(ExcelFolder))
            return FileType.Xlsx;
        if (entryName.StartsWith(PowerPointFolder))
            return FileType.Pptx;
        return FileType.Unknown;
    }
}
