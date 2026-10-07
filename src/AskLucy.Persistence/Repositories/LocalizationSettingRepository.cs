using AskLucy.Application.Localization;
using AskLucy.Domain.Localization;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class LocalizationSettingRepository(AskLucyDbContext dbContext) : ILocalizationSettingRepository
{
    public Task<LocalizationSetting?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.LocalizationSettings.SingleOrDefaultAsync(s => s.Id == LocalizationSetting.SingletonId, cancellationToken);

    public void Add(LocalizationSetting setting) => dbContext.LocalizationSettings.Add(setting);

    public void ExpectRowVersion(LocalizationSetting setting, byte[] rowVersion) =>
        dbContext.Entry(setting).Property(s => s.RowVersion).OriginalValue = rowVersion;
}
