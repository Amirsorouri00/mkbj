using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintArchivePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Legacy_Archived_Row_Without_Previous_Status_Restores_To_Inactive()
    {
        await using var db = CreateDatabase();
        var blueprint = AttributeBlueprint.Create(42, "Color", AttributeBlueprintDataType.Text, Now);
        db.AttributeBlueprints.Add(blueprint);
        // Existing rows have Archived status but no saved previous status.
        db.Entry(blueprint).Property(x => x.Status).CurrentValue = AttributeBlueprintStatus.Archived;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var reloaded = await db.AttributeBlueprints.SingleAsync(TestContext.Current.CancellationToken);
        reloaded.PreviousStatus.Should().BeNull();
        reloaded.Unarchive(Now.AddMinutes(1));

        reloaded.Status.Should().Be(AttributeBlueprintStatus.Inactive);
        reloaded.PreviousStatus.Should().BeNull();
        reloaded.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AttributeBlueprintUnarchivedDomainEvent>()
            .Which.Status.Should().Be(AttributeBlueprintStatus.Inactive);
    }

    private static EwaysVNextDbContext CreateDatabase()
        => new(new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
