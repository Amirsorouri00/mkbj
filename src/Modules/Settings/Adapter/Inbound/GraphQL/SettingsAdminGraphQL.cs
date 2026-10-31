using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Settings.Adapter.Inbound.GraphQL;

[ExtendObjectType(typeof(Query))]
[Authorize(Policy = AuthPolicies.AdminSuper)]
public sealed class SettingsAdminGraphQLQueries
{
    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<SellerView> AdminSellerAsync(
        [Service] ISettingsAdminQueries queries, CancellationToken ct) =>
        queries.GetSellerAsync(ct);

    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<IReadOnlyList<PaymentInstructionVersionView>> AdminPaymentInstructionsAsync(
        [Service] ISettingsAdminQueries queries, CancellationToken ct) =>
        queries.GetPaymentInstructionsAsync(ct);

    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<CommerceLimitsView> AdminCommerceLimitsAsync(
        [Service] ISettingsAdminQueries queries, CancellationToken ct) =>
        queries.GetCommerceLimitsAsync(ct);
}

[ExtendObjectType(typeof(Mutation))]
[Authorize(Policy = AuthPolicies.AdminSuper)]
public sealed class SettingsAdminGraphQLMutations
{
    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<bool> UpdateSellerAsync(
        string legalName, string uen, string address, string email, bool gstRegistered,
        string invoiceFooter, IReadOnlyList<string> requiredBuyerFields,
        ClaimsPrincipal user, [Service] ISettingsAdminService service, CancellationToken ct) =>
        Act(service.UpdateSellerAsync(
            new UpdateSellerCommand(legalName, uen, address, email, gstRegistered, invoiceFooter, requiredBuyerFields),
            Actor(user), ct));

    [RequirePermission(AdminPermissions.SettingsManage)]
    public async Task<int> PublishPaymentInstructionAsync(
        string text, ClaimsPrincipal user,
        [Service] ISettingsAdminService service, CancellationToken ct) =>
        ResultGraphQLExtensions.ValueOrThrow(
            await service.PublishPaymentInstructionAsync(text, Actor(user), ct));

    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<bool> ActivatePaymentInstructionAsync(
        int version, ClaimsPrincipal user,
        [Service] ISettingsAdminService service, CancellationToken ct) =>
        Act(service.ActivatePaymentInstructionAsync(version, Actor(user), ct));

    /// <summary>
    /// Amounts arrive as two-decimal strings here too. The same reasoning as REST applies and,
    /// more sharply: GraphQL has no decimal scalar, so a Float would make rounding the client's
    /// problem on every request.
    /// </summary>
    [RequirePermission(AdminPermissions.SettingsManage)]
    public Task<bool> UpdateCommerceLimitsAsync(
        string minUsd, string maxUsd, int bulkDefaultMax, int compositionDeadlineMs, int compositionMaxLines,
        ClaimsPrincipal user, [Service] ISettingsAdminService service, CancellationToken ct) =>
        Act(service.UpdateCommerceLimitsAsync(
            new UpdateCommerceLimitsCommand(
                UsdMinor(minUsd), UsdMinor(maxUsd), bulkDefaultMax, compositionDeadlineMs, compositionMaxLines),
            Actor(user), ct));

    private static long UsdMinor(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                value ?? string.Empty, @"^\d+\.\d{2}$",
                System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            throw new EwaysVNext.Outcomes.Error("VALIDATION_FAILED",
                $"'{value}' is not a USD amount. Use exactly two decimal places, for example \"25.00\".")
                .ToGraphQLException();
        }

        return MinorUnits.ToMinor(decimal.Parse(value!, CultureInfo.InvariantCulture));
    }

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
}
