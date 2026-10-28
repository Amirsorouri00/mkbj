using System.Reflection;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;
using EwaysVNext.Modules.Location.Adapter.Outbound;
using EwaysVNext.Modules.Location.Application;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Inbound;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public class LocationQueriesTests
{
    private static EwaysVNextDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EwaysVNextDbContext(options);
    }

    private static void SetPrivateProperty(object target, string propertyName, object? value) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static void SeedProvince(EwaysVNextDbContext db, int id, string name, bool isActive = true)
    {
        var province = new Province();
        SetPrivateProperty(province, nameof(Province.Id), id);
        SetPrivateProperty(province, nameof(Province.Name), name);
        SetPrivateProperty(province, nameof(Province.IsActive), isActive);
        db.Provinces.Add(province);
        db.SaveChanges();
    }

    private static void SeedCity(EwaysVNextDbContext db, int id, int provinceId, string name)
    {
        db.Set<City>().Add(new City { Id = id, ProvinceId = provinceId, Name = name, IsActive = true });
        db.SaveChanges();
    }

    private static void SeedDistrict(EwaysVNextDbContext db, int id, int cityId, string name)
    {
        db.Set<District>().Add(new District { Id = id, CityId = cityId, Name = name, IsActive = true });
        db.SaveChanges();
    }

    private static (IProvinceRepository Repository, IProvinceInboundPort Port) CreateServices(EwaysVNextDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLocationOutboundAdapters(new ConfigurationBuilder().Build(), isDevelopment: true);
        services.AddLocationApplication(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IProvinceRepository>(), provider.GetRequiredService<IProvinceInboundPort>());
    }

    [Fact]
    public async Task Provinces_Without_A_Name_Filter_Returns_Every_Active_Province()
    {
        await using var db = CreateDbContext();
        SeedProvince(db, 1, "Tehran");
        SeedProvince(db, 2, "Fars");
        SeedProvince(db, 3, "Retired", isActive: false);
        var (repository, _) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.Provinces(repository);

        result.Select(p => p.Name).Should().BeEquivalentTo(["Tehran", "Fars"]);
    }

    [Fact]
    public async Task Provinces_With_A_Name_Filter_Returns_Only_Matching_Provinces()
    {
        await using var db = CreateDbContext();
        SeedProvince(db, 1, "Tehran");
        SeedProvince(db, 2, "Fars");
        var (repository, _) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.Provinces(repository, name: "Teh");

        result.Select(p => p.Name).Should().BeEquivalentTo(["Tehran"]);
    }

    [Fact]
    public async Task Provinces_Passes_A_Null_Predicate_To_The_Repository_When_No_Name_Is_Given()
    {
        var repository = Substitute.For<IProvinceRepository>();
        repository.GetAllProvincesAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Province, bool>>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Province>>([]));
        var queries = new LocationQueries();

        _ = await queries.Provinces(repository);

        _ = repository.Received(1).GetAllProvincesAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CitiesByProvinceId_Returns_The_Provinces_Cities_With_Their_Districts()
    {
        await using var db = CreateDbContext();
        SeedProvince(db, 1, "Tehran");
        SeedCity(db, 10, 1, "Tehran City");
        SeedDistrict(db, 100, 10, "District One");
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.CitiesByProvinceId(port, 1, CancellationToken.None);

        var city = result.Should().ContainSingle().Subject;
        city.Name.Should().Be("Tehran City");
        city.Districts.Should().ContainSingle(d => d.Name == "District One");
    }

    [Fact]
    public async Task CitiesByProvinceId_Throws_NotFound_When_The_Province_Does_Not_Exist()
    {
        await using var db = CreateDbContext();
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var act = () => queries.CitiesByProvinceId(port, 999, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundGraphQLException>();
    }

    [Fact]
    public async Task DistrictsByCityId_Returns_The_Citys_Districts()
    {
        await using var db = CreateDbContext();
        SeedProvince(db, 1, "Tehran");
        SeedCity(db, 10, 1, "Tehran City");
        SeedDistrict(db, 100, 10, "District One");
        SeedDistrict(db, 101, 10, "District Two");
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.DistrictsByCityId(port, 10, CancellationToken.None);

        result.Select(d => d.Name).Should().BeEquivalentTo(["District One", "District Two"]);
    }

    [Fact]
    public async Task DistrictsByCityId_Throws_NotFound_When_The_City_Does_Not_Exist()
    {
        await using var db = CreateDbContext();
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var act = () => queries.DistrictsByCityId(port, 999, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundGraphQLException>();
    }

    [Fact]
    public async Task CityById_Returns_The_City_When_It_Exists()
    {
        await using var db = CreateDbContext();
        SeedProvince(db, 1, "Tehran");
        SeedCity(db, 10, 1, "Tehran City");
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.CityById(port, 10, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Tehran City");
    }

    [Fact]
    public async Task CityById_Returns_Null_Instead_Of_Throwing_When_The_City_Does_Not_Exist()
    {
        await using var db = CreateDbContext();
        var (_, port) = CreateServices(db);
        var queries = new LocationQueries();

        var result = await queries.CityById(port, 999, CancellationToken.None);

        result.Should().BeNull();
    }
}
