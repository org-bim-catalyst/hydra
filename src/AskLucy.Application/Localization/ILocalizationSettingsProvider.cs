namespace AskLucy.Application.Localization;

/// <summary>
/// The platform localization state, cached for 30 s and evicted when an administrator changes it (FR-044a, research R14), so a toggle
/// applies without a redeploy. A failed read throws; it is never answered with a guess.
/// </summary>
public interface ILocalizationSettingsProvider
{
    Task<LocalizationSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>Drops the cached state so the next read sees the database.</summary>
    void Evict();
}
