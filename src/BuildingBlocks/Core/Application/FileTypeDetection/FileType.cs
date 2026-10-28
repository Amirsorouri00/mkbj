namespace EwaysVNext.Core.Application.FileTypeDetection;

public enum FileType
{
    Unknown = 0,

    Pdf,
    Jpeg,
    Png,
    Gif,
    Bmp,
    Webp,
    Tiff,

    Zip,

    Docx,

    Xlsx,

    Pptx,

    /// <summary>
    /// OLE2 Compound File Binary container. Legacy Office documents (.doc/.xls/.ppt), Outlook .msg
    /// and Windows Installer .msi all share this signature; telling them apart needs the container's
    /// directory stream, which lies outside a bounded header, so they are not distinguished.
    /// </summary>
    OleCompoundFile,

    Gzip,
    SevenZip,
    Rar,
}
