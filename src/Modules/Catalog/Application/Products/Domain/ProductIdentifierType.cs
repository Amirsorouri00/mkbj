namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// The kinds of identifier a product can be matched on, in descending order of how much they
/// prove. A GTIN identifies the article worldwide; a source id identifies it only within the one
/// system it came from, which is why that one carries a domain.
/// </summary>
public enum ProductIdentifierType
{
    Gtin = 1,
    Upc = 2,
    Ean = 3,
    Mpn = 4,
    SourceId = 5
}
