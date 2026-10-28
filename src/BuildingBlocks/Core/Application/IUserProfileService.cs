namespace EwaysVNext.Core.Application;

public interface IUserProfileService
{
    string Email { get; }
    string UserName { get; }
    string PhoneNumber { get; }
    Guid IdentityUserId { get; }
}
