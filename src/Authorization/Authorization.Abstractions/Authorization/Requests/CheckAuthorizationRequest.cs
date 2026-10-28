namespace AuthorizationService.Abstractions.Authorization.Requests;

/// <summary>
/// A permission check.
/// <para>
/// <paramref name="TokenRoleCodes"/> carries roles asserted by the token itself rather than by a
/// stored assignment, which is how backoffice users work: Keycloak owns who is staff and who is a
/// super admin, and this database owns what each role may do. The caller is responsible for
/// passing them only when the token's realm makes them meaningful — a Shop token's roles must
/// arrive here as <c>null</c>, or self-service registration becomes self-service administration.
/// </para>
/// </summary>
public record CheckAuthorizationRequest(
    Guid UserId,
    string PermissionCode,
    Guid? ScopeId,
    IReadOnlyCollection<string>? TokenRoleCodes = null);
