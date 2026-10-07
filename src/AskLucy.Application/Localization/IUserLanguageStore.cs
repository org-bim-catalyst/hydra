namespace AskLucy.Application.Localization;

/// <summary>The language a user chose for the interface and notifications, stored with the account (FR-044b). It isn't the AI response language.</summary>
public interface IUserLanguageStore
{
    /// <summary>The stored choice, even when it is no longer supported; null when the user never chose one or the user doesn't exist.</summary>
    Task<string?> GetPreferredLanguageAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Stores the choice; false when there is no such active user.</summary>
    Task<bool> SetPreferredLanguageAsync(string userId, string language, CancellationToken cancellationToken);
}
