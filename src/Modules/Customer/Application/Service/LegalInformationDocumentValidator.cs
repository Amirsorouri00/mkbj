using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Service;

public static class LegalInformationDocumentValidator
{
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".pdf" };

    private static readonly HashSet<FileType> AllowedFileTypes = [FileType.Pdf, FileType.Jpeg, FileType.Png];

    private static readonly Error ExtensionNotAllowed = new(
        "LEGALINFO_DOCUMENT_EXTENSION_NOT_ALLOWED", "Only JPG, JPEG, PNG and PDF files are allowed.");
    private static readonly Error Empty = new("LEGALINFO_DOCUMENT_EMPTY", "The uploaded file is empty.");
    private static readonly Error TooLarge = new("LEGALINFO_DOCUMENT_TOO_LARGE", "Files cannot exceed 5 MB.");
    private static readonly Error ContentInvalid = new(
        "LEGALINFO_DOCUMENT_CONTENT_INVALID", "The file content does not match an allowed document type.");

    public static Result Validate(string fileName, long sizeBytes, FileTypeDetectionResult detected)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            return Result.Failure(ExtensionNotAllowed);

        if (sizeBytes <= 0)
            return Result.Failure(Empty);

        if (sizeBytes > MaxSizeBytes)
            return Result.Failure(TooLarge);

        if (!AllowedFileTypes.Contains(detected.FileType))
            return Result.Failure(ContentInvalid);

        return Result.Success();
    }
}
