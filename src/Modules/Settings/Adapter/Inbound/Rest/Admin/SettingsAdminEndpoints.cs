using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Settings.Adapter.Inbound.Rest.Admin;

public sealed record UpdateSellerRequest(
    string LegalName,
    string Uen,
    string Address,
    string Email,
    bool GstRegistered,
    string InvoiceFooter,
    IReadOnlyList<string> RequiredBuyerFields);

public sealed record PublishPaymentInstructionRequest(string Text);

/// <summary>Amounts are two-decimal strings; see <see cref="SettingsAdminEndpoints"/>.</summary>
public sealed record UpdateCommerceLimitsRequest(
    string MinUsd,
    string MaxUsd,
    int BulkDefaultMax,
    int CompositionDeadlineMs,
    int CompositionMaxLines);

public sealed partial class SettingsAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/settings";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Settings administration")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.SettingsManage);

        group.MapGet("/seller", async (ISettingsAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetSellerAsync(ct)).ToHttpResult());

        group.MapPut("/seller", (UpdateSellerRequest request, ClaimsPrincipal user,
            ISettingsAdminService service, CancellationToken ct) =>
            Act(user, actor => service.UpdateSellerAsync(
                new UpdateSellerCommand(
                    request.LegalName, request.Uen, request.Address, request.Email,
                    request.GstRegistered, request.InvoiceFooter, request.RequiredBuyerFields),
                actor, ct)));

        group.MapGet("/payment-instructions", async (ISettingsAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetPaymentInstructionsAsync(ct)).ToHttpResult());

        group.MapPost("/payment-instructions", async (
            PublishPaymentInstructionRequest request, ClaimsPrincipal user,
            ISettingsAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.PublishPaymentInstructionAsync(request.Text, actor!, ct);

            return result.ToCreatedHttpResult(version =>
                $"{BasePath}/payment-instructions/{version.ToString(CultureInfo.InvariantCulture)}");
        });

        group.MapPost("/payment-instructions/{version:int}/activate", (
            int version, ClaimsPrincipal user, ISettingsAdminService service, CancellationToken ct) =>
            Act(user, actor => service.ActivatePaymentInstructionAsync(version, actor, ct)));

        group.MapGet("/commerce-limits", async (ISettingsAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetCommerceLimitsAsync(ct)).ToHttpResult());

        group.MapPut("/commerce-limits", (UpdateCommerceLimitsRequest request, ClaimsPrincipal user,
            ISettingsAdminService service, CancellationToken ct) =>
        {
            if (!TryUsdMinor(request.MinUsd, out var min, out var minError))
                return Task.FromResult(Result.Failure(minError!).ToHttpResult());

            if (!TryUsdMinor(request.MaxUsd, out var max, out var maxError))
                return Task.FromResult(Result.Failure(maxError!).ToHttpResult());

            return Act(user, actor => service.UpdateCommerceLimitsAsync(
                new UpdateCommerceLimitsCommand(
                    min, max, request.BulkDefaultMax,
                    request.CompositionDeadlineMs, request.CompositionMaxLines),
                actor, ct));
        });
    }

    /// <summary>
    /// Exactly <c>digits.dd</c>, nothing else.
    /// <para>
    /// Not a lenient decimal parse, deliberately. "25" and "25.0" are almost certainly what the
    /// caller meant, but accepting them means the API quietly decides what a half-written amount
    /// stands for — and the one place that matters is money. Refusing makes the caller say it.
    /// A leading minus is not in the pattern, so a negative amount is a format error rather than
    /// something the domain has to catch later.
    /// </para>
    /// </summary>
    private static bool TryUsdMinor(string? value, out long minor, out Error? error)
    {
        minor = 0;
        error = null;

        if (value is null || !UsdPattern().IsMatch(value))
        {
            error = new Error("VALIDATION_FAILED",
                $"'{value}' is not a USD amount. Use exactly two decimal places, for example \"25.00\".");
            return false;
        }

        minor = MinorUnits.ToMinor(decimal.Parse(value, CultureInfo.InvariantCulture));
        return true;
    }

    [GeneratedRegex(@"^\d+\.\d{2}$")]
    private static partial Regex UsdPattern();

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
