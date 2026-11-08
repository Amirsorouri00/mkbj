using System.Globalization;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Sourcing.Application.Services;

/// <summary>
/// The review queue: a person deciding what the pipeline could not.
/// </summary>
/// <remarks>
/// <para>
/// Every decision is audited with the record it was made about, because these are the judgements
/// that put somebody else's words and pictures into this seller's catalogue. "Who approved
/// publishing this?" has to have an answer years later.
/// </para>
/// <para>
/// Approving does not publish. It makes the record valid, and the next pipeline step picks it
/// up — so a reviewer working through a hundred records is not waiting on a hundred round trips
/// to Catalog, and a publish that fails is retried by the job rather than by the person.
/// </para>
/// </remarks>
internal sealed class StagedRecordReviewService(
    IStagedRecordRepository records,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IStagedRecordReviewService
{
    private const string TargetType = "StagedRecord";

    public Task<Result> ApproveAsync(
        long recordId, long? categoryOverrideId, AdminActor actor, CancellationToken ct = default) =>
        DecideAsync(
            recordId, actor, "approved",
            (record, now) => record.Approve(actor.UserId, categoryOverrideId, now), ct);

    public Task<Result> MergeIntoAsync(
        long recordId, long productId, AdminActor actor, CancellationToken ct = default) =>
        DecideAsync(
            recordId, actor, "merged",
            (record, now) => record.MergeInto(actor.UserId, productId, now), ct);

    public Task<Result> CreateNewAsync(
        long recordId, AdminActor actor, CancellationToken ct = default) =>
        DecideAsync(
            recordId, actor, "create_new",
            (record, now) => record.ChooseCreateNew(actor.UserId, now), ct);

    public Task<Result> RejectAsync(
        long recordId, string reason, AdminActor actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Task.FromResult(Result.Failure(
                SourcingErrors.ValidationFailed("A reason is required to reject a record.")));
        }

        return DecideAsync(
            recordId, actor, "rejected",
            (record, now) => record.Reject(actor.UserId, reason, now), ct);
    }

    private async Task<Result> DecideAsync(
        long recordId,
        AdminActor actor,
        string action,
        Action<StagedRecord, DateTimeOffset> decide,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var record = await records.GetAsync(recordId, ct);
        if (record is null) return Result.Failure(SourcingErrors.RecordNotFound);

        var before = Snapshot(record);

        try
        {
            decide(record, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), $"sourcing.staged_record.{action}", TargetType,
            recordId.ToString(CultureInfo.InvariantCulture), before, Snapshot(record));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <remarks>
    /// The status, the match and the rights approver — no titles, no descriptions, no supplier
    /// text. An audit log is read by people who have no business seeing the content.
    /// </remarks>
    private static object Snapshot(StagedRecord record) => new
    {
        record.Status,
        Match = record.Match.Kind,
        record.Match.ProductId,
        record.RightsApprovedBy,
        IssueCodes = record.Issues.Select(issue => issue.Code).ToList(),
    };

    private static Error FromDomain(DomainException exception) => exception.Code switch
    {
        "STAGED_RECORD_HAS_ERRORS" => new Error(
            "STAGED_RECORD_HAS_ERRORS", exception.Message, 422),
        "STAGED_RECORD_ALREADY_PUBLISHED" => new Error(
            "STAGED_RECORD_ALREADY_PUBLISHED", exception.Message, 409),
        _ => SourcingErrors.RecordStateConflict,
    };
}

/// <summary>Source rights policies, which are the one import decision with legal weight.</summary>
internal sealed class SourcePolicyService(
    ISourcePolicyRepository policies,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ISourcePolicyService
{
    private const string TargetType = "SourcePolicy";

    public async Task<Result<long>> CreateAsync(
        string domain,
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Domain.SourcePolicies.SourcePolicy policy;
        try
        {
            policy = Domain.SourcePolicies.SourcePolicy.Create(
                ids.Generate(), domain, crawlAllowed, contentUseAllowed, imageUseAllowed, notes,
                clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(SourcingErrors.ValidationFailed(exception.Message));
        }

        // Checked before inserting as well as by the unique index. The index is what decides a
        // race; this is what gives an administrator a sentence instead of a constraint name.
        if (await policies.FindByDomainAsync(policy.Domain, ct) is not null)
            return Result.Failure<long>(SourcingErrors.PolicyDomainTaken);

        await policies.AddAsync(policy, ct);

        audit.Record(
            actor.ToAudit(), "sourcing.source_policy.created", TargetType,
            policy.Id.ToString(CultureInfo.InvariantCulture), null, Snapshot(policy));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(policy.Id);
    }

    public async Task<Result> UpdateAsync(
        long policyId,
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Tracked, unlike ListAsync's rows: an update read through a no-tracking query saves
        // nothing and reports success, which is the worst pair of behaviours to combine.
        var policy = await policies.GetAsync(policyId, ct);

        if (policy is null)
            return Result.Failure(new Error("NOT_FOUND", "No such source policy.", 404));

        var before = Snapshot(policy);

        try
        {
            policy.Update(crawlAllowed, contentUseAllowed, imageUseAllowed, notes, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(SourcingErrors.ValidationFailed(exception.Message));
        }

        audit.Record(
            actor.ToAudit(), "sourcing.source_policy.updated", TargetType,
            policyId.ToString(CultureInfo.InvariantCulture), before, Snapshot(policy));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static object Snapshot(Domain.SourcePolicies.SourcePolicy policy) => new
    {
        policy.Domain,
        policy.CrawlAllowed,
        policy.ContentUseAllowed,
        policy.ImageUseAllowed,
    };
}
