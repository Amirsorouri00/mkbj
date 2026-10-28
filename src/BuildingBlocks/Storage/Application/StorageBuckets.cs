namespace EwaysVNext.Storage;

/// <summary>
/// The three buckets (ADR-0017 D11). They are separate because their access rules differ:
/// published media is servable, while staged downloads and invoice documents are private and
/// reached only through short-lived presigned URLs.
/// </summary>
public static class StorageBuckets
{
    /// <summary>Published product images.</summary>
    public const string CatalogMedia = "catalog-media";

    /// <summary>Raw import downloads and uploaded files. Private; never served directly.</summary>
    public const string ImportStaging = "import-staging";

    /// <summary>Invoice and credit-note PDFs. Private; presigned URLs only.</summary>
    public const string Documents = "documents";

    public static IReadOnlyList<string> All { get; } = [CatalogMedia, ImportStaging, Documents];
}
