using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.Events;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Events.Application;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
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

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : ModuleDbContextBase(options, "test")
    {
        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();
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

    [Fact]
    public async Task DeactivateProductAsync_Deactivates_And_Saves_With_No_Explicit_Dispatch_Call()
    {
        var productId = new SnowflakeIdGenerator().Generate();
        var repo = Substitute.For<IProductRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        var product = Product.Create(productId, Sku.Create($"T-{Guid.NewGuid():N}"[..12]), "P", Money.Create(5m), TestTime.Now, 10);
        repo.GetByIdAsync(productId, Arg.Any<CancellationToken>()).Returns(product);

        var sut = new CatalogAdminService(
            Substitute.For<Modules.Catalog.Application.Products.Ports.Inbound.IProductService>(),
            repo,
            uow,
            new FixedClock());

        var result = await sut.DeactivateProductAsync(productId);

        result.IsSuccess.Should().BeTrue();
        product.IsActive.Should().BeFalse();
        product.DomainEvents.Should().Contain(e => e is ProductDeactivatedDomainEvent);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
