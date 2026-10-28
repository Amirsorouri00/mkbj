namespace EwaysVNext.Core.Application.FileTypeDetection;

public static class FileTypeMetadata
{
    public static string? GetMimeType(FileType fileType) => fileType switch
    {
        FileType.Pdf => "application/pdf",
        FileType.Jpeg => "image/jpeg",
        FileType.Png => "image/png",
        FileType.Gif => "image/gif",
        FileType.Bmp => "image/bmp",
        FileType.Webp => "image/webp",
        FileType.Tiff => "image/tiff",
        FileType.Zip => "application/zip",
        FileType.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        FileType.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        FileType.Pptx => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        FileType.OleCompoundFile => "application/x-ole-storage",
        FileType.Gzip => "application/gzip",
        FileType.SevenZip => "application/x-7z-compressed",
        FileType.Rar => "application/vnd.rar",
        _ => null,
    };

    public static string? GetExtension(FileType fileType) => fileType switch
    {
        FileType.Pdf => ".pdf",
        FileType.Jpeg => ".jpg",
        FileType.Png => ".png",
        FileType.Gif => ".gif",
        FileType.Bmp => ".bmp",
        FileType.Webp => ".webp",
        FileType.Tiff => ".tiff",
        FileType.Zip => ".zip",
        FileType.Docx => ".docx",
        FileType.Xlsx => ".xlsx",
        FileType.Pptx => ".pptx",
        FileType.Gzip => ".gz",
        FileType.SevenZip => ".7z",
        FileType.Rar => ".rar",
        _ => null,
    };
}
