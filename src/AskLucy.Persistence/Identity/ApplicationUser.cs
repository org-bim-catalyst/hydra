using AskLucy.Domain.Chats;
using AskLucy.Domain.Consent;
using Microsoft.AspNetCore.Identity;

namespace AskLucy.Persistence.Identity;

/// <summary>
/// The Identity user, migrated from the legacy <c>AskLucy.Areas.Identity.Models.ApplicationUser</c>.
/// Lives in Persistence (not Domain) because it must derive from <see cref="IdentityUser"/> —
/// an ASP.NET Core type — which would violate Domain purity (constitution &#167;3). See
/// research.md Topic 1/6 and data-model.md &#167; ApplicationUser.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public DateOnly BirthDate { get; set; }

    /// <summary>
    /// Replaces the legacy inline <c>byte[] ProfilePicture</c> BLOB (FR-025): the avatar is
    /// now stored as a file and served via a signed URL.
    /// </summary>
    public string? AvatarFileName { get; set; }

    /// <summary>
    /// When this account was created. <see cref="IdentityUser"/> has no equivalent field, and
    /// this type cannot inherit <see cref="AskLucy.Domain.Common.BaseEntity"/> (conflicting
    /// <c>Id</c> types), so it is set explicitly at every creation call site rather than by the
    /// generic audit interceptor. Drives the admin dashboard's registration-trend chart
    /// (SPEC-001 FR-003). See specs/001-admin-dashboard/research.md Topic 1.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Soft-delete flag (SPEC-001 FR-016). Kept as an explicit column, not derived from
    /// <see cref="DeletedAtUtc"/>, so the EF Core global query filter is a simple, indexable predicate.</summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public ICollection<UserChat> UserChats { get; set; } = new List<UserChat>();

    /// <summary>Append-only consent-decision history (specs/004-cookie-consent-privacy); cascade-deleted with the account.</summary>
    public ICollection<CookieConsentRecord> CookieConsentRecords { get; set; } = new List<CookieConsentRecord>();
}
