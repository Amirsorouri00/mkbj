namespace EwaysVNext.Core.Application.FileTypeDetection;


public readonly record struct FileTypeDetectionResult(FileType FileType)
{
    public static FileTypeDetectionResult Unknown => default;

    /// <summary><see langword="true"/> when a supported signature matched.</summary>
    public bool IsDetected => FileType != FileType.Unknown;

    /// <inheritdoc cref="FileTypeMetadata.GetMimeType"/>
    public string? MimeType => FileTypeMetadata.GetMimeType(FileType);

    /// <inheritdoc cref="FileTypeMetadata.GetExtension"/>
    public string? Extension => FileTypeMetadata.GetExtension(FileType);
}
