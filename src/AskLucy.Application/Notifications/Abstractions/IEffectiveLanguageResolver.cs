namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Picks the language a notification renders in (FR-044, research R14).</summary>
public interface IEffectiveLanguageResolver
{
    /// <param name="recipientUserId">Null for a recipient-less delivery (support mailbox).</param>
    /// <param name="explicitLanguage">The request's explicit language, the first candidate.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<string> ResolveAsync(string? recipientUserId, string? explicitLanguage, CancellationToken cancellationToken);
}
