using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// One published wording of the payment instruction.
/// <para>
/// Immutable once written, because an invoice that has already gone out quotes the version it was
/// issued under. Editing the text in place would retroactively change what a customer was told to
/// do, with no record that it ever said anything else.
/// </para>
/// </summary>
public sealed class PaymentInstructionVersion : Entity<long>
{
    private PaymentInstructionVersion()
    {
    }

    public long PaymentInstructionId { get; private set; }

    public int Version { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Null for the seeded version, which no person published.</summary>
    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static PaymentInstructionVersion Create(
        long id, long instructionId, int version, string text, Guid? createdBy, DateTimeOffset now)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException("VALIDATION_FAILED", "The payment instruction text is required.");

        if (trimmed.Length > 4000)
            throw new DomainException("VALIDATION_FAILED", "The payment instruction cannot exceed 4000 characters.");

        return new PaymentInstructionVersion
        {
            Id = id,
            PaymentInstructionId = instructionId,
            Version = version,
            Text = trimmed,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
