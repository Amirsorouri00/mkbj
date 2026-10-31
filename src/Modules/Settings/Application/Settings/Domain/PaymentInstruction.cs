using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// How a customer is told to pay, and every wording it has ever had.
/// <para>
/// The versions live inside this aggregate rather than beside it because "exactly one version is
/// active" is the invariant. As two aggregates, publishing would be two writes with a window in
/// between where either none or two versions were active — and that window is when a customer
/// loads their pro forma.
/// </para>
/// </summary>
public sealed class PaymentInstruction : AggregateRoot<long>
{
    private readonly List<PaymentInstructionVersion> _versions = [];

    private PaymentInstruction()
    {
    }

    public int ActiveVersion { get; private set; }

    public IReadOnlyList<PaymentInstructionVersion> Versions => _versions;

    public PaymentInstructionVersion Active =>
        _versions.SingleOrDefault(version => version.Version == ActiveVersion)
        ?? throw new DomainException("CONFLICT",
            $"Version {ActiveVersion} is marked active but does not exist.");

    public static PaymentInstruction CreateDefault(DateTimeOffset now)
    {
        var instruction = new PaymentInstruction { Id = SettingsDefaults.SingletonId };

        instruction._versions.Add(PaymentInstructionVersion.Create(
            SettingsDefaults.SingletonId,
            SettingsDefaults.SingletonId,
            version: 1,
            SettingsDefaults.InitialPaymentInstructionText,
            createdBy: null,
            now));

        instruction.ActiveVersion = 1;

        return instruction;
    }

    /// <summary>
    /// Appends a new wording and makes it current in one step. Publishing without activating
    /// would leave a version nobody is using and an admin wondering why the change had no effect.
    /// </summary>
    public PaymentInstructionVersion Publish(long versionId, string text, Guid createdBy, DateTimeOffset now)
    {
        var next = _versions.Count == 0 ? 1 : _versions.Max(version => version.Version) + 1;

        var published = PaymentInstructionVersion.Create(versionId, Id, next, text, createdBy, now);

        _versions.Add(published);
        ActiveVersion = next;

        return published;
    }

    /// <summary>Returns to an earlier wording, which is how a bad edit is undone.</summary>
    public void Activate(int version)
    {
        if (_versions.All(existing => existing.Version != version))
            throw new DomainException("NOT_FOUND", $"Payment instruction version {version} does not exist.");

        ActiveVersion = version;
    }
}
