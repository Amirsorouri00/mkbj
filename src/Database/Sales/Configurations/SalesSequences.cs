using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Database.Sales.Configurations;

/// <summary>
/// The gapless number sequences orders and documents draw from.
/// </summary>
/// <remarks>
/// Four separate sequences, not one shared counter: an auditor reading INV-000001 through
/// INV-000400 can see that no invoice is missing, which a shared counter would make impossible.
/// They never cycle — a wrapped invoice number would collide with one issued years earlier.
/// </remarks>
public static class SalesSequences
{
    public const string Schema = "sales";

    public const string OrderNumber = "order_number_seq";
    public const string ProFormaNumber = "pf_number_seq";
    public const string CommercialNumber = "inv_number_seq";
    public const string CreditNoteNumber = "cn_number_seq";

    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var name in new[] { OrderNumber, ProFormaNumber, CommercialNumber, CreditNoteNumber })
        {
            modelBuilder.HasSequence<long>(name, Schema).StartsAt(1).IncrementsBy(1);
        }
    }
}
