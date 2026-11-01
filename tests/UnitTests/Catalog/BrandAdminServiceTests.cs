using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Brands.Services;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// The brand admin service's job is mostly refusal: the alias index is global, so most of what an
/// admin can type is already someone else's. These pin which refusals the admin sees as a
/// conflict rather than as a database error.
/// </summary>
public sealed class BrandAdminServiceTests
{
    private const long BrandId = 4242;

    private readonly IBrandRepository _repository = Substitute.For<IBrandRepository>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly ICatalogChangeNotifier _notifier = Substitute.For<ICatalogChangeNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();
    private readonly AdminActor _actor = new(Guid.NewGuid(), IsSuperAdmin: false);

    private BrandAdminService CreateService()
    {
        _ids.Generate().Returns(_ => Random.Shared.NextInt64(1, long.MaxValue));
        return new BrandAdminService(_repository, _audit, _notifier, _unitOfWork, _ids, _clock);
    }

    private Brand Existing(string displayName, params string[] aliases)
    {
        var brand = Brand.Create(BrandId, displayName, "admin", _clock.UtcNow);
        var id = 1L;
        foreach (var alias in aliases) brand.AddAlias(id++, alias, _clock.UtcNow);
        return brand;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_Rejects_Name_Used_As_Another_Brands_Alias()
    {
        _repository.FindByNameOrAliasAsync("North Wind", Arg.Any<CancellationToken>())
            .Returns(Existing("Northwind", "North Wind"));

        var result = await CreateService().CreateAsync("North Wind", [], _actor, Ct);

        result.Error!.Code.Should().Be("BRAND_NAME_DUPLICATE",
            "the alias index spans every brand, so a name already used as an alias is taken");
        result.Error.HttpStatus.Should().Be(409);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Brand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Rejects_An_Alias_Another_Brand_Already_Claims()
    {
        _repository.FindByNameOrAliasAsync("Acme", Arg.Any<CancellationToken>())
            .Returns(Existing("Acme Corp", "Acme"));

        var result = await CreateService().CreateAsync("Brand New", ["Acme"], _actor, Ct);

        result.Error!.Code.Should().Be("BRAND_NAME_DUPLICATE",
            "an alias is checked for the same reason the name is");
    }

    [Fact]
    public async Task Create_Adds_The_Brand_With_Its_Aliases_And_Saves()
    {
        var result = await CreateService().CreateAsync("Northwind", ["North Wind"], _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<Brand>(brand =>
                brand.CanonicalName == "northwind" &&
                brand.Provenance == "admin" &&
                brand.Aliases.Count == 1),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Records_Audit()
    {
        await CreateService().CreateAsync("Northwind", [], _actor, Ct);

        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "brand.created", "Brand", Arg.Any<string>(),
            Arg.Is<object?>(before => before == null), Arg.Any<object>());
    }

    [Fact]
    public async Task SetActive_Calls_SellabilityChanged_After_Save()
    {
        _repository.GetByIdAsync(BrandId, Arg.Any<CancellationToken>()).Returns(Existing("Northwind"));

        await CreateService().SetActiveAsync(BrandId, false, _actor, Ct);

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.SellabilityChangedAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Renaming_Only_Notifies_A_Display_Change()
    {
        _repository.GetByIdAsync(BrandId, Arg.Any<CancellationToken>()).Returns(Existing("Northwind"));

        await CreateService().RenameAsync(BrandId, "Northwind Trading", _actor, Ct);

        await _notifier.Received(1).DisplayChangedAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().SellabilityChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_Allows_A_Name_This_Brand_Already_Answers_To()
    {
        var brand = Existing("Northwind", "Northwind Trading");
        _repository.GetByIdAsync(BrandId, Arg.Any<CancellationToken>()).Returns(brand);
        _repository.FindByNameOrAliasAsync("Northwind Trading", Arg.Any<CancellationToken>()).Returns(brand);

        var result = await CreateService().RenameAsync(BrandId, "Northwind Trading", _actor, Ct);

        result.IsSuccess.Should().BeTrue(
            "promoting one of a brand's own aliases to its name is not a collision with anyone");
        brand.CanonicalName.Should().Be("northwind-trading");
    }

    [Fact]
    public async Task Rename_Reports_A_Missing_Brand()
    {
        _repository.GetByIdAsync(BrandId, Arg.Any<CancellationToken>()).Returns((Brand?)null);

        var result = await CreateService().RenameAsync(BrandId, "Anything", _actor, Ct);

        result.Error!.Code.Should().Be("BRAND_NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404);
    }

    [Fact]
    public async Task SetAliases_Replaces_The_Whole_List()
    {
        var brand = Existing("Northwind", "North Wind", "Nordwind");
        _repository.GetByIdAsync(BrandId, Arg.Any<CancellationToken>()).Returns(brand);

        await CreateService().SetAliasesAsync(BrandId, ["Nordwind", "NW Labs"], _actor, Ct);

        brand.Aliases.Select(alias => alias.Alias)
            .Should().BeEquivalentTo(["nordwind", "nw-labs"],
                "an admin editing the list sees the whole list, so a set is a replacement");
    }

    [Fact]
    public async Task EnsureAsync_Returns_The_Existing_Brand_Without_Writing()
    {
        _repository.FindByNameOrAliasAsync("Northwind", Arg.Any<CancellationToken>())
            .Returns(Existing("Northwind"));

        var result = await CreateService().EnsureAsync("Northwind", [], "import", Ct);

        result.Value.Should().Be(BrandId);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Brand>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureAsync_Creates_With_The_Given_Provenance()
    {
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.Saved);

        var result = await CreateService().EnsureAsync("Northwind", ["North Wind"], "import", Ct);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<Brand>(brand => brand.Provenance == "import"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureAsync_Reloads_When_Another_Worker_Won_The_Race()
    {
        var winner = Existing("Northwind");
        _repository.FindByNameOrAliasAsync("Northwind", Arg.Any<CancellationToken>())
            .Returns((Brand?)null, winner);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.UniqueViolation);

        var result = await CreateService().EnsureAsync("Northwind", [], "import", Ct);

        result.IsSuccess.Should().BeTrue(
            "both workers wanted this brand to exist, and after the race it does");
        result.Value.Should().Be(BrandId);
    }

    [Fact]
    public async Task EnsureAsync_Records_No_Audit_Entry()
    {
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.Saved);

        await CreateService().EnsureAsync("Northwind", [], "import", Ct);

        _audit.DidNotReceive().Record(
            Arg.Any<AuditActor>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task A_Name_That_Canonicalises_To_Nothing_Is_A_Domain_Error_Not_A_Crash()
    {
        var result = await CreateService().CreateAsync(" -- ", [], _actor, Ct);

        result.Error!.Code.Should().Be("CANONICAL_KEY_EMPTY");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
