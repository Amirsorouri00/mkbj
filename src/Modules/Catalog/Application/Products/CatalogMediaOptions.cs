namespace EwaysVNext.Modules.Catalog.Application.Products;

/// <summary>Bound from <c>Catalog:Media</c>.</summary>
public sealed class CatalogMediaOptions
{
    public const string SectionName = "Catalog:Media";

    /// <summary>
    /// 10 MiB. Large enough for a photograph straight off a phone, small enough that a few
    /// concurrent uploads cannot exhaust the host.
    /// </summary>
    public long MaxBytes { get; set; } = 10_485_760;
}
