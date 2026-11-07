using System.Net.Http.Headers;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// A host with three partner keys whose authentication is stubbed.
/// </summary>
/// <remarks>
/// The real authenticator needs issued keys, a pepper and live rows; none of that is what these
/// tests are about. The stub keeps the part that matters — which partner and which scopes a key
/// carries — so the tests still exercise tenancy and scope checks rather than skipping them.
/// </remarks>
internal static class SalesApiFactory
{
    public const long PartnerAId = 9001;
    public const long PartnerBId = 9002;
    public const long PartnerNoCustomerWriteId = 9003;

    public const string PartnerAKey = "pk_partnerA_test";
    public const string PartnerBKey = "pk_partnerB_test";
    public const string NoCustomerWriteKey = "pk_nocustw_test";

    public static WebApplicationFactory<Program> Create(
        PostgresFixture fixture, Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return fixture.CreateFactory(configureServices: services =>
        {
            services.RemoveAll<IApiKeyAuthenticator>();
            services.AddScoped<IApiKeyAuthenticator, StubApiKeyAuthenticator>();

            configure?.Invoke(services);
        });
    }

    /// <summary>Makes every composition run out of time before it starts.</summary>
    public static void WithNoCompositionTime(IServiceCollection services)
    {
        services.RemoveAll<ICompositionBudgetFactory>();
        services.AddSingleton<ICompositionBudgetFactory, ExpiredBudgetFactory>();
    }

    /// <summary>Narrows the candidate pool to the given variants.</summary>
    /// <remarks>
    /// A real catalogue of 400 variants can compose almost any amount, which makes "there is no
    /// basket for this" hard to arrange honestly. Restricting the pool produces a genuinely
    /// impossible request rather than a mocked refusal.
    /// </remarks>
    public static Action<IServiceCollection> WithPoolLimitedTo(params long[] variantIds) =>
        services =>
        {
            services.AddSingleton(new PoolFilter(variantIds));
            services.Decorate<ICandidatePoolGateway, FilteredCandidatePoolGateway>();
        };

    /// <summary>
    /// Serves one captured pool on the first call, then the real thing.
    /// </summary>
    /// <remarks>
    /// This is how a pool goes stale on purpose: the first composition sees a variant that has
    /// since been withdrawn, and the live re-quote has to catch it.
    /// </remarks>
    public static void WithStaleFirstPool(IServiceCollection services) =>
        services.Decorate<ICandidatePoolGateway, StaleOncePoolGateway>();

    /// <summary>Makes every contact sync fail.</summary>
    public static void WithFailingContactSync(IServiceCollection services) =>
        services.Decorate<ISalesCustomerGateway, FailingSyncCustomerGateway>();

    public static HttpClient Client(WebApplicationFactory<Program> factory, string key)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return client;
    }

    private sealed class StubApiKeyAuthenticator : IApiKeyAuthenticator
    {
        private static readonly IReadOnlySet<string> AllScopes = PartnerScopes.All;

        private static readonly IReadOnlySet<string> WithoutCustomerWrite =
            new HashSet<string>(StringComparer.Ordinal)
            {
                PartnerScopes.OrdersCreate,
                PartnerScopes.OrdersRead,
                PartnerScopes.CustomersRead,
                PartnerScopes.InvoicesRead,
            };

        public Task<ApiKeyAuthResult?> AuthenticateAsync(
            string presentedKey, CancellationToken ct = default) =>
            Task.FromResult(presentedKey switch
            {
                PartnerAKey => new ApiKeyAuthResult(PartnerAId, 1, AllScopes, 100_000, 100),
                PartnerBKey => new ApiKeyAuthResult(PartnerBId, 2, AllScopes, 100_000, 50),
                NoCustomerWriteKey =>
                    new ApiKeyAuthResult(PartnerNoCustomerWriteId, 3, WithoutCustomerWrite, 100_000, 50),

                // Every other key fails the same way, saying nothing about why — as the real one
                // does.
                _ => null,
            });
    }
}

/// <summary>The variants a filtered pool is allowed to offer.</summary>
internal sealed record PoolFilter(IReadOnlyCollection<long> VariantIds);

internal sealed class FilteredCandidatePoolGateway(
    ICandidatePoolGateway inner, PoolFilter filter) : ICandidatePoolGateway
{
    public async Task<Result<CandidatePool>> GetAsync(
        string currency, DateTimeOffset now, CancellationToken ct = default)
    {
        var pool = await inner.GetAsync(currency, now, ct);

        return pool.IsFailure
            ? pool
            : Result.Success(pool.Value! with
            {
                Candidates =
                    [.. pool.Value.Candidates.Where(c => filter.VariantIds.Contains(c.VariantId))],
            });
    }

    public void Invalidate(string currency) => inner.Invalidate(currency);
}

internal sealed class StaleOncePoolGateway(ICandidatePoolGateway inner) : ICandidatePoolGateway
{
    private static CandidatePool? _captured;
    private static bool _served;

    public static void Reset()
    {
        _captured = null;
        _served = false;
    }

    public async Task<Result<CandidatePool>> GetAsync(
        string currency, DateTimeOffset now, CancellationToken ct = default)
    {
        var pool = await inner.GetAsync(currency, now, ct);
        if (pool.IsFailure) return pool;

        if (_captured is null)
        {
            _captured = pool.Value!;
            return pool;
        }

        if (!_served)
        {
            _served = true;
            return Result.Success(_captured);
        }

        return pool;
    }

    public void Invalidate(string currency) => inner.Invalidate(currency);
}

internal sealed class FailingSyncCustomerGateway(ISalesCustomerGateway inner) : ISalesCustomerGateway
{
    public Task<SalesPartnerCustomer?> FindPartnerCustomerAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default) =>
        inner.FindPartnerCustomerAsync(partnerId, externalCustomerId, ct);

    public Task<Result<SalesPartnerCustomer>> CreatePartnerCustomerAsync(
        long partnerId, string externalCustomerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default) =>
        inner.CreatePartnerCustomerAsync(partnerId, externalCustomerId, contact, correlationId, ct);

    public Task<Result> SyncPartnerContactAsync(
        long customerId, long partnerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("Customer is unreachable.");

    public Task<Result<SalesInvoiceParty>> GetInvoicePartyAsync(
        long customerId, CancellationToken ct = default) =>
        inner.GetInvoicePartyAsync(customerId, ct);

    public Task<Result<SalesWebsiteCustomer>> EnsureWebsiteCustomerAsync(
        Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default) =>
        inner.EnsureWebsiteCustomerAsync(identityUserId, email, emailVerified, ct);
}

internal sealed class ExpiredBudgetFactory : ICompositionBudgetFactory
{
    public ICompositionBudget Start(TimeSpan deadline) => new ExpiredBudget();
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToArray())
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>
    /// Wraps the registered <typeparamref name="TService"/> in <typeparamref name="TDecorator"/>.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a replacement, so these tests still run the real gateway and only
    /// bend the one behaviour under test. A wholesale stub would pass while the real thing was
    /// broken.
    /// </remarks>
    public static void Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var existing = services.Last(d => d.ServiceType == typeof(TService));
        services.Remove(existing);

        services.Add(new ServiceDescriptor(
            typeof(TService),
            provider => ActivatorUtilities.CreateInstance<TDecorator>(
                provider, Resolve(provider, existing)),
            existing.Lifetime));
    }

    private static object Resolve(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
}
