using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.Events;
using EwaysVNext.Events.Application;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public class EventDispatchingTests
{
    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() => Id = Guid.NewGuid();
        public void RaiseTestEvent(IDomainEvent @event) => Raise(@event);
    }

    private sealed record TestEvent : DomainEvent;

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // InMemory has no xmin: keep RowVersion an inert property, as EwaysVNextDbContext does.
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(e => e.GetProperties()).Where(p => p.Name == "RowVersion"))
            {
                property.IsConcurrencyToken = false;
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }

            base.OnModelCreating(modelBuilder);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task EfCoreUnitOfWork_Dispatches_Raised_Events_And_Clears_Buffer()
    {
        var eventBus = Substitute.For<IDomainEventBus>();
        await using var db = CreateContext();
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(db, eventBus);

        var aggregate = new TestAggregate();
        var @event = new TestEvent();
        aggregate.RaiseTestEvent(@event);
        db.Aggregates.Add(aggregate);

        await unitOfWork.SaveChangesAsync();

        await eventBus.Received(1).PublishAsync((IDomainEvent)@event, Arg.Any<CancellationToken>());
        aggregate.DomainEvents.Should().BeEmpty("the unit of work must clear the buffer after dispatch");
    }

    [Fact]
    public async Task EfCoreUnitOfWork_Only_Dispatches_Events_Raised_Since_The_Last_Save()
    {
        var eventBus = Substitute.For<IDomainEventBus>();
        await using var db = CreateContext();
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(db, eventBus);

        var aggregate = new TestAggregate();
        aggregate.RaiseTestEvent(new TestEvent());
        db.Aggregates.Add(aggregate);
        await unitOfWork.SaveChangesAsync();

        aggregate.RaiseTestEvent(new TestEvent());
        await unitOfWork.SaveChangesAsync();

        await eventBus.Received(2).PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
    }
}
