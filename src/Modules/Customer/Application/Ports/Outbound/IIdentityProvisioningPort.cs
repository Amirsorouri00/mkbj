using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;


public interface IIdentityProvisioningPort
{
    
    Task<Result<Guid>> CreateUserAsync(string username, string initialPassword, CancellationToken ct = default);

    
    Task<Result> UpdateUsernameAsync(Guid identityUserId, string newUsername, CancellationToken ct = default);

   
    Task<Result> DisableUserAsync(Guid identityUserId, CancellationToken ct = default);

    
    Task<Result> DeleteUserAsync(Guid identityUserId, CancellationToken ct = default);
}
