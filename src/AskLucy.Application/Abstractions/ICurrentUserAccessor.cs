namespace AskLucy.Application.Abstractions;

/// <summary>
/// Resolves the acting user id (constitution &#167;5 auditing, and ownership checks in
/// command handlers). Implemented in AskLucy.Web (reads the authenticated principal).
/// </summary>
public interface ICurrentUserAccessor
{
    string? UserId { get; }

    /// <summary>Whether the acting user's authenticated principal holds <paramref name="role"/> — drives the FR-014 role-escalation guard.</summary>
    bool IsInRole(string role);
}
