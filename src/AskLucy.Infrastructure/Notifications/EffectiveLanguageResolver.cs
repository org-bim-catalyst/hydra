using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// English only until localization ships (US8 replaces this with the full FR-044 chain). English is
/// the platform's default language, so every notification renders in it for now.
/// </summary>
public sealed class EffectiveLanguageResolver : IEffectiveLanguageResolver
{
    public const string DefaultLanguage = "en";

    public Task<string> ResolveAsync(string? recipientUserId, string? explicitLanguage, CancellationToken cancellationToken) =>
        Task.FromResult(DefaultLanguage);
}
