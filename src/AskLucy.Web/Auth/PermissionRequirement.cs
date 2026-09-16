using Microsoft.AspNetCore.Authorization;

namespace AskLucy.Web.Auth;

/// <summary>Satisfied when the caller's principal carries at least one of <see cref="AnyOf"/> as a <c>permission</c> claim (research.md Decision 4).</summary>
public sealed class PermissionRequirement(params string[] anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}
