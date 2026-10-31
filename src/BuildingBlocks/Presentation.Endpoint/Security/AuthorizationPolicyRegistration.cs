using System.Security.Claims;

using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace EwaysVNext.Presentation.Endpoint.Security;

public static class AuthorizationPolicyRegistration
{
    /// <summary>
    /// Registers the realm policies. Partner scope policies are registered separately, because
    /// they need the scope requirement handler that ships with the Partners module.
    /// </summary>
    public static AuthorizationOptions AddEwaysAuthorizationPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AuthPolicies.AdminStaff, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            .RequireRole(AdminRoles.Staff, AdminRoles.SuperAdmin));

        options.AddPolicy(AuthPolicies.AdminSuper, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            .RequireRole(AdminRoles.SuperAdmin));

        // The Shop realm gate carries no role requirement: every authenticated customer is the
        // same kind of customer, and what they may reach is decided by ownership of the row, not
        // by a role.
        options.AddPolicy(AuthPolicies.ShopCustomer, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(EwaysClaimTypes.Tenant, AdminRoles.ShopRealm));

        // One policy per scope. They authenticate with the API-key scheme ONLY: a backoffice JWT
        // must not satisfy a partner endpoint, because a partner endpoint answers "for this
        // partner" and a human token names no partner.
        foreach (var scope in PartnerScopes.All)
        {
            options.AddPolicy(AuthPolicies.Partner(scope), policy => policy
                .AddAuthenticationSchemes(AuthSchemes.PartnerApiKey)
                .RequireAuthenticatedUser()
                .AddRequirements(new PartnerScopeRequirement(scope)));
        }

        return options;
    }

    /// <summary>
    /// The policy names Catalog's endpoints were written against, kept working while those
    /// endpoints still reference them. Removed in phase 2–3, when Catalog is regated.
    /// </summary>
    public static AuthorizationOptions AddLegacyPolicyAliases(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy("Admin", policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            .RequireRole(AdminRoles.Staff, AdminRoles.SuperAdmin));

        // A customer-facing read an admin must also be able to make, so either realm satisfies it.
        options.AddPolicy("Customer", policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                context.User.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.ShopRealm)
                || (context.User.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
                    && (context.User.IsInRole(AdminRoles.Staff) || context.User.IsInRole(AdminRoles.SuperAdmin)))));

        return options;
    }

    /// <summary>Convenience for a principal built by hand in a test or a tool.</summary>
    public static bool IsBackofficeAdmin(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            && (user.IsInRole(AdminRoles.Staff) || user.IsInRole(AdminRoles.SuperAdmin));
    }
}
