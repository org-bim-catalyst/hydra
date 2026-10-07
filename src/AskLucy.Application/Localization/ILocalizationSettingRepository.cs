using AskLucy.Domain.Localization;

namespace AskLucy.Application.Localization;

public interface ILocalizationSettingRepository
{
    /// <summary>The singleton row, tracked so the caller can change it; null before the migration seeded it.</summary>
    Task<LocalizationSetting?> GetAsync(CancellationToken cancellationToken);

    void Add(LocalizationSetting setting);

    /// <summary>Makes the next save fail with a concurrency conflict unless the row still has <paramref name="rowVersion"/>.</summary>
    void ExpectRowVersion(LocalizationSetting setting, byte[] rowVersion);
}
