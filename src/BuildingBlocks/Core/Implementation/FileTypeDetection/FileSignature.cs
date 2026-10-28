using EwaysVNext.Core.Application.FileTypeDetection;

namespace EwaysVNext.Core.Implementation.FileTypeDetection;

internal sealed class FileSignature
{
    public delegate FileType Refiner(ReadOnlySpan<byte> content);

    private readonly (int Offset, byte[] Bytes)[] _segments;
    private readonly Refiner? _refine;

    public FileSignature(FileType fileType, ReadOnlySpan<byte> magic, Refiner? refine = null)
        : this(fileType, [(0, magic.ToArray())], refine)
    {
    }

    public FileSignature(FileType fileType, (int Offset, byte[] Bytes)[] segments, Refiner? refine = null)
    {
        FileType = fileType;
        _segments = segments;
        _refine = refine;
    }

    public FileType FileType { get; }

    public FileType Match(ReadOnlySpan<byte> content)
    {
        foreach (var (offset, bytes) in _segments)
        {
            if (content.Length < offset + bytes.Length || !content.Slice(offset, bytes.Length).SequenceEqual(bytes))
                return FileType.Unknown;
        }

        return _refine is null ? FileType : _refine(content);
    }
}
