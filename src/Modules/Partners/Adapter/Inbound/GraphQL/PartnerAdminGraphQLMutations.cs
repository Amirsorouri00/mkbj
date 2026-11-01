using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL;

/// <summary>
/// The key is here and nowhere else: this payload is the only time the plaintext exists outside
/// the holder's hands, and no read ever returns it.
/// </summary>
public sealed record IssuedApiKeyPayload(
    long KeyId,
    string Prefix,
    string Key,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt);

[ExtendObjectType(typeof(Mutation))]
public sealed class PartnerAdminGraphQLMutations
{
    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public async Task<long> CreatePartnerAsync(
        string name, IReadOnlyList<string> scopes, int? rateLimitPerMinute, int? bulkMaxItems,
        ClaimsPrincipal user, [Service] IPartnerAdminService service, CancellationToken ct) =>
        ResultGraphQLExtensions.ValueOrThrow(await service.CreateAsync(
            new CreatePartnerCommand(name, scopes, rateLimitPerMinute, bulkMaxItems), Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<bool> SuspendPartnerAsync(
        long id, ClaimsPrincipal user, [Service] IPartnerAdminService service, CancellationToken ct) =>
        Act(service.SuspendAsync(id, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<bool> ReactivatePartnerAsync(
        long id, ClaimsPrincipal user, [Service] IPartnerAdminService service, CancellationToken ct) =>
        Act(service.ReactivateAsync(id, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<bool> SetPartnerScopesAsync(
        long id, IReadOnlyList<string> scopes, ClaimsPrincipal user,
        [Service] IPartnerAdminService service, CancellationToken ct) =>
        Act(service.SetScopesAsync(id, scopes, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<bool> SetPartnerLimitsAsync(
        long id, int rateLimitPerMinute, int bulkMaxItems, ClaimsPrincipal user,
        [Service] IPartnerAdminService service, CancellationToken ct) =>
        Act(service.SetLimitsAsync(id, rateLimitPerMinute, bulkMaxItems, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public async Task<IssuedApiKeyPayload> IssuePartnerApiKeyAsync(
        long id, IReadOnlyList<string>? scopes, DateTimeOffset? expiresAt, ClaimsPrincipal user,
        [Service] IPartnerAdminService service, CancellationToken ct) =>
        Payload(ResultGraphQLExtensions.ValueOrThrow(
            await service.IssueKeyAsync(id, scopes, expiresAt, Actor(user), ct)));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public async Task<IssuedApiKeyPayload> RotatePartnerApiKeyAsync(
        long id, long keyId, double? overlapHours, ClaimsPrincipal user,
        [Service] IPartnerAdminService service, CancellationToken ct) =>
        Payload(ResultGraphQLExtensions.ValueOrThrow(await service.RotateKeyAsync(
            id, keyId,
            overlapHours is { } hours ? TimeSpan.FromHours(hours) : null,
            Actor(user), ct)));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<bool> RevokePartnerApiKeyAsync(
        long id, long keyId, ClaimsPrincipal user,
        [Service] IPartnerAdminService service, CancellationToken ct) =>
        Act(service.RevokeKeyAsync(id, keyId, Actor(user), ct));

    /// <summary>
    /// The policy has already established a Backoffice super admin, so a principal that cannot
    /// produce an actor here is a contradiction rather than a refusal — it means the token passed
    /// the gate and then failed to parse, which is a fault, not a permission problem.
    /// </summary>
    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The token passed the admin policy but carries no usable subject.")
                .SetCode("UNAUTHORIZED")
                .Build());

    private static async Task<bool> Act(Task<Result> action)
    {
        (await action).ThrowIfFailure();
        return true;
    }

    private static IssuedApiKeyPayload Payload(IssuedApiKey issued) =>
        new(issued.KeyId, issued.Prefix, issued.PlaintextKey, issued.Scopes, issued.ExpiresAt);
}
