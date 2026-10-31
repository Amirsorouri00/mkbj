using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.IntegrationTests.Support;

/// <summary>
/// Creates partners through the real admin service rather than by inserting rows, so a test
/// exercises the same issue and rotate paths production uses — including the hashing.
/// </summary>
internal static class PartnerSeeder
{
    private static readonly AdminActor Super = new(Guid.NewGuid(), IsSuperAdmin: true);

    public static async Task<(long PartnerId, long KeyId, string Key)> CreateAsync(
        WebApplicationFactory<Program> factory, IReadOnlyCollection<string> scopes, int rateLimit = 300)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPartnerAdminService>();

        var partnerId = (await service.CreateAsync(
            new CreatePartnerCommand($"Test {Guid.NewGuid():N}", scopes, rateLimit, 100), Super)).Value;

        var issued = (await service.IssueKeyAsync(partnerId, null, null, Super)).Value!;

        return (partnerId, issued.KeyId, issued.PlaintextKey);
    }

    public static async Task SuspendAsync(WebApplicationFactory<Program> factory, long partnerId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPartnerAdminService>()
            .SuspendAsync(partnerId, Super);
    }

    public static async Task RevokeAsync(WebApplicationFactory<Program> factory, long partnerId, long keyId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPartnerAdminService>()
            .RevokeKeyAsync(partnerId, keyId, Super);
    }

    public static async Task<string> RotateAsync(
        WebApplicationFactory<Program> factory, long partnerId, long keyId, TimeSpan overlap)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPartnerAdminService>()
            .RotateKeyAsync(partnerId, keyId, overlap, Super);

        return result.Value!.PlaintextKey;
    }
}
