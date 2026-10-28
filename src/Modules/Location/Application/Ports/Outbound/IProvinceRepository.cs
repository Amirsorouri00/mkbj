
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using System.Linq.Expressions;

namespace EwaysVNext.Modules.Location.Application.Ports.Outbound;

public interface IProvinceRepository
{
    Task<IList<Province>> GetAllProvincesAsync(Expression<Func<Province, bool>>? predicate = null, CancellationToken ct = default);
    Task<Province?> GetProvinceAsync(Expression<Func<Province, bool>> predicate,bool includeCities = true, CancellationToken ct = default);
}

