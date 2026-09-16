using AskLucy.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace AskLucy.Web.Auth;

/// <summary>Checks the requirement against the <c>permission</c> claims <see cref="CurrentAuthorizationClaimsTransformation"/> already attached to the principal — no extra lookup here.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.FindAll(PermissionClaims.Type).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        if (requirement.AnyOf.Any(granted.Contains))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
