using System.Text;
namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

internal static class ProductMessages
{
    public const string NameEmpty = "Product name cannot be empty.";
    public const string NameTooLong = "Product name cannot exceed 200 characters.";
    public const string StockNegative = "Initial stock cannot be negative.";
    public const string InactiveCannotBeModified = "Inactive product cannot be modified.";
    public const string ResultingStockNegative = "Resulting stock cannot be negative.";
    public const string InactiveCannotBeReserved = "Inactive product cannot be reserved.";
    public const string ReservationQuantity = "Quantity must be positive.";
    public const string StockInsufficientFormat = "Only {0} available.";
    public const string ReservationAlreadyCommitted =
        "This reservation was already committed; use RevertCommitment to undo a commitment.";
    public const string ReservationNotFound = "Reservation does not exist.";
    public const string ReservationNotCommitted = "No committed reservation exists for this id.";
    public const string RetentionNegative = "Retention must not be negative.";
    public static readonly CompositeFormat StockInsufficientComposite = CompositeFormat.Parse(StockInsufficientFormat);
}
