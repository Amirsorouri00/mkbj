using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Settings.Application.Settings.Services;

public sealed class SettingsAdminService(
    ISettingsRepository repository,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ISettingsAdminService
{
    private const string TargetType = "Settings";
    private static readonly string SingletonId =
        SettingsDefaults.SingletonId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public async Task<Result> UpdateSellerAsync(
        UpdateSellerCommand command, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure(Error.Forbidden);

        ArgumentNullException.ThrowIfNull(command);

        var seller = await repository.GetSellerAsync(tracking: true, ct);
        if (seller is null)
            return Result.Failure(Error.NotFound);

        var before = Snapshot(seller);

        try
        {
            seller.Update(
                command.LegalName, command.Uen, command.Address, command.Email,
                command.GstRegistered, command.InvoiceFooter, command.RequiredBuyerFields);
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        audit.Record(actor.ToAudit(), "settings.seller_updated", TargetType, SingletonId,
            before, Snapshot(seller));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<int>> PublishPaymentInstructionAsync(
        string text, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure<int>(Error.Forbidden);

        var instruction = await repository.GetPaymentInstructionAsync(tracking: true, ct);
        if (instruction is null)
            return Result.Failure<int>(Error.NotFound);

        PaymentInstructionVersion published;
        try
        {
            published = instruction.Publish(ids.Generate(), text, actor.UserId, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(new Error(ex.Code, ex.Message));
        }

        // The text is in the audit entry on purpose: this is the record of what customers were
        // told to do, and reconstructing it from the versions table alone would depend on that
        // table still holding the row.
        audit.Record(actor.ToAudit(), "settings.payment_instruction_published", TargetType, SingletonId,
            before: null,
            after: new { published.Version, published.Text });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(published.Version);
    }

    public async Task<Result> ActivatePaymentInstructionAsync(
        int version, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure(Error.Forbidden);

        var instruction = await repository.GetPaymentInstructionAsync(tracking: true, ct);
        if (instruction is null)
            return Result.Failure(Error.NotFound);

        var before = instruction.ActiveVersion;

        try
        {
            instruction.Activate(version);
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        audit.Record(actor.ToAudit(), "settings.payment_instruction_activated", TargetType, SingletonId,
            before: new { ActiveVersion = before },
            after: new { ActiveVersion = version });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdateCommerceLimitsAsync(
        UpdateCommerceLimitsCommand command, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure(Error.Forbidden);

        ArgumentNullException.ThrowIfNull(command);

        var limits = await repository.GetCommerceLimitsAsync(tracking: true, ct);
        if (limits is null)
            return Result.Failure(Error.NotFound);

        var before = Snapshot(limits);

        try
        {
            limits.Update(
                command.MinUsdMinor, command.MaxUsdMinor, command.BulkDefaultMax,
                command.CompositionDeadlineMs, command.CompositionMaxLines);
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        audit.Record(actor.ToAudit(), "settings.limits_updated", TargetType, SingletonId,
            before, Snapshot(limits));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static object Snapshot(SellerProfile seller) => new
    {
        seller.LegalName,
        seller.Uen,
        seller.Address,
        seller.Email,
        seller.GstRegistered,
        seller.InvoiceFooter,
        RequiredBuyerFields = seller.RequiredBuyerFields.ToArray(),
    };

    private static object Snapshot(CommerceLimits limits) => new
    {
        limits.MinUsdMinor,
        limits.MaxUsdMinor,
        limits.BulkDefaultMax,
        limits.CompositionDeadlineMs,
        limits.CompositionMaxLines,
    };
}
