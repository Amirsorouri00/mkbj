using EwaysVNext.Modules.Partners.Application.Usage;

namespace EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;

/// <summary>
/// Accepts a usage entry for later writing.
/// <para>
/// It never blocks and never throws. Usage logging is bookkeeping: it must not be able to slow a
/// partner's request down, and it must not be able to fail one. When the buffer is full the entry
/// is dropped and <c>false</c> returned, which is a deliberate trade — losing a log line is
/// recoverable, stalling the API under load is not.
/// </para>
/// </summary>
public interface IApiKeyUsageRecorder
{
    bool TryRecord(ApiKeyUsageEntry entry);
}
