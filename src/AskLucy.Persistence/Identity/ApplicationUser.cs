using AskLucy.Domain.Chats;
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

    public ICollection<UserChat> UserChats { get; set; } = new List<UserChat>();
}
