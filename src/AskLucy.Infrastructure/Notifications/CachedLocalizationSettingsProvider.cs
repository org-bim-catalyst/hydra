using AskLucy.Application.Localization;
using AskLucy.Domain.Localization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// The platform localization state, read from the database and cached for 30 s (research R14). An administrator's change evicts it,
/// so it takes effect at once on this instance and within 30 s on any other. A database failure propagates: the caller must not
/// pretend localization is on or off.
/// </summary>
public sealed class CachedLocalizationSettingsProvider(IMemoryCache cache, IServiceScopeFactory scopeFactory) : ILocalizationSettingsProvider
{
    public const string CacheKey = "localization:setting";

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<LocalizationSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out LocalizationSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var setting = await scope.ServiceProvider.GetRequiredService<ILocalizationSettingRepository>().GetAsync(cancellationToken);

        // Before the migration's seed row exists the platform is, by definition, English only.
        var snapshot = setting is null
            ? LocalizationSnapshot.EnglishOnly
            : new LocalizationSnapshot(setting.IsEnabled, setting.SupportedLanguages);
        cache.Set(CacheKey, snapshot, CacheDuration);
        return snapshot;
    }

    public void Evict() => cache.Remove(CacheKey);
}
