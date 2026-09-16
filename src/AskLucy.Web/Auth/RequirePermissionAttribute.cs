using Microsoft.AspNetCore.Authorization;

namespace AskLucy.Web.Auth;

/// <summary>
/// Endpoint permission gate (research.md Decision 4): the caller must hold at least one of
/// <paramref name="anyOf"/> as an effective permission. Stacking multiple attributes on one
/// action ANDs them (e.g. class-level View + method-level Manage). The three role-management
/// screens and privileged-role changes stay on the reserved <c>AdministratorOrSuperUser</c>
/// policy instead (FR-002) — this attribute is for the catalogue-area endpoints research.md
/// Decision 5 maps out.
/// </summary>
public sealed class RequirePermissionAttribute(params string[] anyOf)
    : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement(anyOf);
    }
}
