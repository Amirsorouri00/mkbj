using System.Security.Claims;
using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sourcing.Application;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Sourcing.Adapter.Inbound.Rest;

public sealed record CreateImportJobRequest(
    string? Mode,
    IReadOnlyList<string>? Sources,
    IReadOnlyList<string>? Queries,
    long? TargetCategoryId,
    IReadOnlyList<string>? IncludeDomains,
    IReadOnlyList<string>? ExcludeDomains,
    int? MaxPages,
    int? MaxDepth,
    int? MaxRecords,
    string? PublishPolicy,
    bool FileRightsAttested);

public sealed record SetMappingRequest(IReadOnlyDictionary<string, string>? Mapping);

public sealed record ApproveRecordRequest(long? CategoryOverrideId);

public sealed record MergeRecordRequest(long ProductId);

public sealed record RejectRecordRequest(string? Reason);

public sealed record SourcePolicyRequest(
    string? Domain, bool CrawlAllowed, bool ContentUseAllowed, bool ImageUseAllowed, string? Notes);

/// <summary>
/// The import surface: jobs, the review queue and source rights policies.
/// </summary>
/// <remarks>
/// <para>
/// Gated twice like every admin surface. Jobs and the review queue are staff work under
/// <c>imports.manage</c>; <b>source rights policies are super-admin only</b>, because a policy
/// decides whether this seller may republish somebody else's words and pictures, and that is not
/// a decision to leave to whoever is clearing the queue today.
/// </para>
/// </remarks>
public sealed class SourcingAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/imports";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup(BasePath)
            .WithTags("Imports")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.ImportsManage);

        staff.MapGet("/jobs", async (
            ISourcingAdminQueries queries, CancellationToken ct,
            string? status = null, int page = 1, int pageSize = 50) =>
            Result.Success(await queries.ListJobsAsync(status, page, pageSize, ct)).ToHttpResult());

        staff.MapGet("/jobs/{id:long}", async (
            long id, ISourcingAdminQueries queries, CancellationToken ct) =>
            (await queries.GetJobAsync(id, ct)).ToHttpResult());

        staff.MapPost("/jobs", async (
            CreateImportJobRequest request, ClaimsPrincipal user,
            IImportJobService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor)) return EndpointResult.Forbidden();

            var spec = ToSpec(request);
            if (spec.IsFailure) return EndpointResult.Fail(spec.Error!);

            return (await service.CreateAsync(spec.Value!, actor!, ct))
                .ToCreatedHttpResult(id => $"{BasePath}/jobs/{id}");
        });

        staff.MapPost("/jobs/{id:long}/start", (
            long id, ClaimsPrincipal user, IImportJobService service, CancellationToken ct) =>
            Act(user, actor => service.StartAsync(id, actor, ct)));

        staff.MapPost("/jobs/{id:long}/pause", (
            long id, ClaimsPrincipal user, IImportJobService service, CancellationToken ct) =>
            Act(user, actor => service.PauseAsync(id, actor, ct)));

        staff.MapPost("/jobs/{id:long}/resume", (
            long id, ClaimsPrincipal user, IImportJobService service, CancellationToken ct) =>
            Act(user, actor => service.ResumeAsync(id, actor, ct)));

        staff.MapPost("/jobs/{id:long}/cancel", (
            long id, ClaimsPrincipal user, IImportJobService service, CancellationToken ct) =>
            Act(user, actor => service.CancelAsync(id, actor, ct)));

        staff.MapPut("/jobs/{id:long}/mapping", (
            long id, SetMappingRequest request, ClaimsPrincipal user,
            IImportFileService files, CancellationToken ct) =>
            Act(user, actor => files.SetMappingAsync(
                id, request.Mapping ?? new Dictionary<string, string>(), actor, ct)));

        staff.MapGet("/records", async (
            ISourcingAdminQueries queries, CancellationToken ct,
            long? jobId = null, string? status = null, string? sourceDomain = null,
            long? cursor = null, int pageSize = 50) =>
            (await queries.ListRecordsAsync(
                new StagedRecordFilter(jobId, status, sourceDomain, cursor, pageSize), ct))
                .ToHttpResult());

        staff.MapGet("/records/{id:long}", async (
            long id, ISourcingAdminQueries queries, CancellationToken ct) =>
            (await queries.GetRecordAsync(id, ct)).ToHttpResult());

        staff.MapPost("/records/{id:long}/approve", (
            long id, ApproveRecordRequest request, ClaimsPrincipal user,
            IStagedRecordReviewService review, CancellationToken ct) =>
            Act(user, actor => review.ApproveAsync(id, request.CategoryOverrideId, actor, ct)));

        staff.MapPost("/records/{id:long}/merge", (
            long id, MergeRecordRequest request, ClaimsPrincipal user,
            IStagedRecordReviewService review, CancellationToken ct) =>
            Act(user, actor => review.MergeIntoAsync(id, request.ProductId, actor, ct)));

        staff.MapPost("/records/{id:long}/create-new", (
            long id, ClaimsPrincipal user,
            IStagedRecordReviewService review, CancellationToken ct) =>
            Act(user, actor => review.CreateNewAsync(id, actor, ct)));

        staff.MapPost("/records/{id:long}/reject", (
            long id, RejectRecordRequest request, ClaimsPrincipal user,
            IStagedRecordReviewService review, CancellationToken ct) =>
            Act(user, actor => review.RejectAsync(id, request.Reason ?? string.Empty, actor, ct)));

        // Rights policies decide whether this seller may republish somebody else's material.
        var super = app.MapGroup($"{BasePath}/policies")
            .WithTags("Imports")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.SettingsManage);

        super.MapPost("/", async (
            SourcePolicyRequest request, ClaimsPrincipal user,
            ISourcePolicyService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor)) return EndpointResult.Forbidden();

            return (await service.CreateAsync(
                request.Domain ?? string.Empty, request.CrawlAllowed, request.ContentUseAllowed,
                request.ImageUseAllowed, request.Notes, actor!, ct))
                .ToCreatedHttpResult(id => $"{BasePath}/policies/{id}");
        });

        super.MapPut("/{id:long}", (
            long id, SourcePolicyRequest request, ClaimsPrincipal user,
            ISourcePolicyService service, CancellationToken ct) =>
            Act(user, actor => service.UpdateAsync(
                id, request.CrawlAllowed, request.ContentUseAllowed, request.ImageUseAllowed,
                request.Notes, actor, ct)));
    }

    /// <remarks>
    /// Parsed here rather than bound, so an unknown mode is a sentence the administrator can act
    /// on instead of a model-binding failure naming a C# type.
    /// </remarks>
    private static Result<CreateImportJobSpec> ToSpec(CreateImportJobRequest request)
    {
        if (!Enum.TryParse<ImportMode>(request.Mode, ignoreCase: true, out var mode))
        {
            return Result.Failure<CreateImportJobSpec>(SourcingErrors.ValidationFailed(
                $"mode must be one of: {string.Join(", ", Enum.GetNames<ImportMode>())}."));
        }

        var policy = Enum.TryParse<PublishPolicy>(request.PublishPolicy, true, out var parsed)
            ? parsed
            : PublishPolicy.AutoPublishValidated;

        var limits = new ImportLimits(
            request.MaxPages ?? ImportLimits.Default.MaxPages,
            request.MaxDepth ?? ImportLimits.Default.MaxDepth,
            request.MaxRecords ?? ImportLimits.Default.MaxRecords);

        return Result.Success(new CreateImportJobSpec(
            mode,
            request.Sources ?? [],
            request.Queries ?? [],
            request.TargetCategoryId,
            request.IncludeDomains ?? [],
            request.ExcludeDomains ?? [],
            limits,
            policy,
            request.FileRightsAttested));
    }

    private static async Task<IResult> Act(
        ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor)) return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
