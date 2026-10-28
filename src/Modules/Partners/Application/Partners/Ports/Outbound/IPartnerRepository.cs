using EwaysVNext.Modules.Partners.Application.Partners.Domain;

namespace EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;

/// <summary>Loads and adds the aggregate, with its keys. Saving is the unit of work's job.</summary>
public interface IPartnerRepository
{
    Task<Partner?> GetAsync(long partnerId, CancellationToken ct = default);

    void Add(Partner partner);
}
