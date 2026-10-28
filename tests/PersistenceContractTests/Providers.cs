using AuthorizationService.Implementation.Persistence;
using EwaysVNext.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EwaysVNext.PersistenceContractTests;

/// <summary>
/// Two providers, because neither alone sees the whole mapping contract:
/// Npgsql is pointed at a <b>refused port</b>, so the relational model is built and LINQ is
/// translated but nothing can execute — that is what catches untranslatable shapes; EF InMemory
/// actually executes, which is what catches materialization failures a connection error would mask.
/// </summary>
public static class Providers
{
    /// <summary>Port 1 is never listening: translation happens before connection.</summary>
    private const string RefusedPort =
        "Host=127.0.0.1;Port=1;Database=contract;Username=contract;Password=contract;Timeout=1";

    public static TContext Npgsql<TContext>() where TContext : DbContext =>
        Create<TContext>(new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(RefusedPort)
            .UseSnakeCaseNamingConvention()
            .Options);

    public static TContext InMemory<TContext>() where TContext : DbContext =>
        Create<TContext>(new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase($"contract-{Guid.NewGuid():N}")
            .Options);

    /// <summary>
    /// The design-time model, not <c>context.Model</c>: the runtime model is read-optimized and
    /// throws for check constraints and Postgres extensions, which is exactly what this suite asserts on.
    /// </summary>
    public static IModel Model(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model;

    private static TContext Create<TContext>(DbContextOptions<TContext> options) where TContext : DbContext =>
        typeof(TContext) == typeof(EwaysVNextDbContext)
            ? (TContext)(object)new EwaysVNextDbContext((DbContextOptions<EwaysVNextDbContext>)(object)options)
            : (TContext)(object)new AuthorizationDbContext((DbContextOptions<AuthorizationDbContext>)(object)options);
}
