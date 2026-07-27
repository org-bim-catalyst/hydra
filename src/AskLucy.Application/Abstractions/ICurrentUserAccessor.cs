namespace AskLucy.Application.Abstractions;

/// <summary>
/// Resolves the acting user id (constitution &#167;5 auditing, and ownership checks in
/// command handlers). Implemented in AskLucy.WebAPI (reads the authenticated principal).
/// </summary>
public interface ICurrentUserAccessor
{
    string? UserId { get; }
}
