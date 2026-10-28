using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Xunit;

namespace EwaysVNext.UnitTests;

public class ProductReservationTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    private static Product ProductWithStock(int stock) =>
        Product.Create(NextId(), Sku.Create($"T-{Guid.NewGuid():N}"[..12]), "Test", Money.Create(10m),
            TestTime.Now, stock);

    [Fact]
    public void Reserve_Reduces_AvailableStock()
    {
        var p = ProductWithStock(10);
        p.ReserveStock(4, NextId(), TimeSpan.FromMinutes(5), TestTime.Now);

        p.AvailableStock.Should().Be(6);
        p.ReservedStock.Should().Be(4);
    }

    [Fact]
    public void Reserve_More_Than_Available_Is_Rejected()
    {
        var p = ProductWithStock(3);
        var act = () => p.ReserveStock(5, NextId(), TimeSpan.FromMinutes(5), TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "STOCK_INSUFFICIENT");
    }

    [Fact]
    public void Release_Returns_Reserved_Stock_And_Is_Idempotent()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(4, id, TimeSpan.FromMinutes(5), TestTime.Now);

        p.ReleaseReservation(id, TestTime.Now);
        p.ReleaseReservation(id, TestTime.Now);

        p.AvailableStock.Should().Be(10);
        p.ReservedStock.Should().Be(0);
    }

    [Fact]
    public void Release_After_Commit_Does_Not_Corrupt_ReservedStock()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromMinutes(5), TestTime.Now);
        p.CommitReservation(id, TestTime.Now);

        p.Stock.Should().Be(7);
        p.ReservedStock.Should().Be(0);

        var release = () => p.ReleaseReservation(id, TestTime.Now);

        release.Should().Throw<DomainException>()
            .Where(e => e.Code == "RESERVATION_ALREADY_COMMITTED",
                "releasing a committed reservation must be refused — undoing a commitment is RevertCommitment's job");

        p.Stock.Should().Be(7);
        p.ReservedStock.Should().Be(0);
        AssertStockInvariants(p);
    }

    [Fact]
    public void Revert_Is_The_Exact_Inverse_Of_Commit()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromMinutes(5), TestTime.Now);
        var (stockAfterReserve, reservedAfterReserve) = (p.Stock, p.ReservedStock);

        p.CommitReservation(id, TestTime.Now);
        p.RevertCommitment(id, TestTime.Now);

        p.Stock.Should().Be(stockAfterReserve);
        p.ReservedStock.Should().Be(reservedAfterReserve);
        AssertStockInvariants(p);
    }

    [Fact]
    public void Release_Is_Allowed_Again_After_A_Commitment_Is_Reverted()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromMinutes(5), TestTime.Now);
        p.CommitReservation(id, TestTime.Now);
        p.RevertCommitment(id, TestTime.Now);

        p.ReleaseReservation(id, TestTime.Now);

        p.Stock.Should().Be(10);
        p.ReservedStock.Should().Be(0);
        p.AvailableStock.Should().Be(10);
        AssertStockInvariants(p);
    }

    private static void AssertStockInvariants(Product product)
    {
        product.ReservedStock.Should().BeGreaterThanOrEqualTo(0, "ReservedStock must never go negative");
        product.Stock.Should().BeGreaterThanOrEqualTo(0, "Stock must never go negative");
        product.AvailableStock.Should().BeLessThanOrEqualTo(product.Stock,
            "AvailableStock may never exceed physical stock — that is what overselling looks like");
    }

    [Fact]
    public void Commit_Deducts_From_Both_Reserved_And_Total_Stock()
    {
        var p = ProductWithStock(10);
        p.ReserveStock(4, NextId(), TimeSpan.FromMinutes(5), TestTime.Now);

        p.CommitReservation(p.Reservations.Single().Id, TestTime.Now);

        p.Stock.Should().Be(6);
        p.ReservedStock.Should().Be(0);
        p.AvailableStock.Should().Be(6);
    }

    [Fact]
    public void ReleaseExpiredReservations_Releases_Only_Ttl_Elapsed_Ones()
    {
        var p = ProductWithStock(10);
        var expiredId = NextId();
        var liveId = NextId();

        p.ReserveStock(3, expiredId, TimeSpan.FromMinutes(10), TestTime.Now);
        p.ReserveStock(2, liveId, TimeSpan.FromHours(1), TestTime.Now);

        var released = p.ReleaseExpiredReservations(TestTime.PlusMinutes(30));

        released.Should().Equal(expiredId);
        p.ReservedStock.Should().Be(2);
        p.AvailableStock.Should().Be(8);
    }

    [Fact]
    public void ReleaseExpiredReservations_With_Nothing_Expired_Returns_Empty()
    {
        var p = ProductWithStock(5);
        p.ReserveStock(1, NextId(), TimeSpan.FromHours(1), TestTime.Now);

        p.ReleaseExpiredReservations(TestTime.Now).Should().BeEmpty();
    }

    [Fact]
    public void Prune_Deletes_A_Committed_Row_Past_Retention_Without_Touching_Stock()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromHours(1), TestTime.Now);
        p.CommitReservation(id, TestTime.Now);

        var (stock, reserved) = (p.Stock, p.ReservedStock);

        var pruned = p.PruneCommittedReservations(TestTime.Now, TimeSpan.Zero);

        pruned.Should().Equal([id]);
        p.Reservations.Should().BeEmpty();
        p.Stock.Should().Be(stock, "commit already settled the arithmetic; pruning is pure GC");
        p.ReservedStock.Should().Be(reserved);
        p.AvailableStock.Should().Be(7);
    }

    [Fact]
    public void Prune_Keeps_A_Committed_Row_Inside_Its_Retention_Window()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromMinutes(5), TestTime.Now);
        p.CommitReservation(id, TestTime.Now);

        p.PruneCommittedReservations(TestTime.Now, TimeSpan.FromDays(90)).Should().BeEmpty(
            "a fresh commitment must stay revertible — cancelling a CONFIRMED order needs the row");

        p.Reservations.Should().ContainSingle();
    }

    [Fact]
    public void Prune_Never_Touches_An_Uncommitted_Hold_However_Old()
    {
        var p = ProductWithStock(10);
        p.ReserveStock(4, NextId(), TimeSpan.FromMinutes(5), TestTime.Now);

        p.PruneCommittedReservations(TestTime.PlusDays(1825), TimeSpan.Zero)
            .Should().BeEmpty();

        p.Reservations.Should().ContainSingle();
        p.ReservedStock.Should().Be(4, "the hold is still counted, so only a release may remove it");
    }

    [Fact]
    public void Reverting_A_Commitment_Clears_Its_Commit_Timestamp()
    {
        var p = ProductWithStock(10);
        var id = NextId();
        p.ReserveStock(3, id, TimeSpan.FromHours(1), TestTime.Now);

        p.Reservations.Single().CommittedAt.Should().BeNull("a hold has not been committed");

        p.CommitReservation(id, TestTime.Now);
        p.Reservations.Single().CommittedAt.Should().NotBeNull();

        p.RevertCommitment(id, TestTime.Now);
        var r = p.Reservations.Single();
        r.IsCommitted.Should().BeFalse();
        r.CommittedAt.Should().BeNull("a reverted row must not be prunable as a committed one");

        p.PruneCommittedReservations(TestTime.PlusDays(1825), TimeSpan.Zero)
            .Should().BeEmpty();
    }

    [Fact]
    public void Prune_Rejects_A_Negative_Retention()
    {
        var p = ProductWithStock(1);

        var act = () => p.PruneCommittedReservations(TestTime.Now, TimeSpan.FromDays(-1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("RETENTION_NEGATIVE");
    }
}
