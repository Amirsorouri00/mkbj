using System.Security.Claims;
using EwaysVNext.Core.Application;
using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Core.Implementation;

public sealed class UserProfileService(IHttpContextAccessor httpContextAccessor) : IUserProfileService
{
    public string Email => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    public string UserName => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
    public string PhoneNumber => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.MobilePhone) ?? string.Empty;
    public Guid IdentityUserId => Guid.Parse(httpContextAccessor.HttpContext?.User.FindFirstValue("identity_id")!);
}
