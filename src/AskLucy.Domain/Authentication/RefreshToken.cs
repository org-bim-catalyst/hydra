namespace AskLucy.Domain.Authentication;

/// <summary>
/// A rotating JWT refresh token. Reuse of an already-rotated token revokes the whole
/// <see cref="TokenFamilyId"/>, per constitution &#167;8 and research.md Topic 1.
/// Intentionally does not derive from <c>BaseEntity</c>/soft-delete: revoked tokens are
/// kept (not hidden) since they are audit-relevant.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public Guid TokenFamilyId { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;

    private RefreshToken()
    {
        // Required by EF Core materialization.
    }

    public static RefreshToken IssueNew(string userId, string tokenHash, Guid tokenFamilyId, TimeSpan lifetime)
    {
        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            TokenFamilyId = tokenFamilyId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime),
        };
    }

    public void Revoke()
    {
        RevokedAtUtc ??= DateTime.UtcNow;
    }
}
