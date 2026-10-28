using Microsoft.AspNetCore.Authorization;

namespace AuthorizationService.Abstractions.Requirements;

public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
