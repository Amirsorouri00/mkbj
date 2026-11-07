using System.Net.Http.Headers;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
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

    public static WebApplicationFactory<Program> Create(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return fixture.CreateFactory(configureServices: services =>
        {
            services.RemoveAll<IApiKeyAuthenticator>();
            services.AddScoped<IApiKeyAuthenticator, StubApiKeyAuthenticator>();
        });
    }

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

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToArray())
        {
            services.Remove(descriptor);
        }
    }
}
