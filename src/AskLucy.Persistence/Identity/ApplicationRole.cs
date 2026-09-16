using Microsoft.AspNetCore.Identity;

namespace AskLucy.Persistence.Identity;

/// <summary>
/// Extends ASP.NET Identity's <see cref="IdentityRole"/> with the fields role management needs
/// (research.md Decision 1). Permission grants themselves are stored as <c>AspNetRoleClaims</c>
/// rows (Decision 1b), not a column here.
/// </summary>
public sealed class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }

    /// <summary>True for the two protected roles (Administrator, Super User) — never renamed, never deleted, always the full permission catalogue (data-model.md).</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
    }
}
