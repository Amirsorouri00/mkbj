namespace EwaysVNext.Core.Application.FileTypeDetection;

public interface IFileTypeDetector
{
    int HeaderLength { get; }

    FileTypeDetectionResult Detect(ReadOnlySpan<byte> content);
}
