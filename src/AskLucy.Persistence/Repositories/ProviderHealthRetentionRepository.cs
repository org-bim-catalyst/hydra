using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ProviderHealthRetentionRepository(AskLucyDbContext dbContext) : IProviderHealthRetentionRepository
{
    public Task<ProviderHealthRetentionSetting?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.ProviderHealthRetentionSettings
            .SingleOrDefaultAsync(s => s.Id == ProviderHealthRetentionSetting.SingletonId, cancellationToken);

    public void ExpectRowVersion(ProviderHealthRetentionSetting setting, byte[] rowVersion) =>
        dbContext.Entry(setting).Property(s => s.RowVersion).OriginalValue = rowVersion;

    public void AddAudit(ProviderHealthRetentionAuditLog entry) => dbContext.ProviderHealthRetentionAuditLogs.Add(entry);
}
