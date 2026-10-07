using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class NotificationTemplateRepository(AskLucyDbContext dbContext) : INotificationTemplateRepository
{
    public Task<NotificationTemplateVersion?> GetPublishedVersionAsync(
        string type, NotificationChannel channel, string language, CancellationToken cancellationToken) =>
        dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Type == type && t.Channel == channel && t.Language == language)
            .SelectMany(t => t.Versions)
            .SingleOrDefaultAsync(v => v.Status == TemplateVersionStatus.Published, cancellationToken);

    public async Task<IReadOnlySet<NotificationTemplateKey>> GetExistingKeysAsync(CancellationToken cancellationToken)
    {
        var keys = await dbContext.NotificationTemplates
            .AsNoTracking()
            .Select(t => new { t.Type, t.Channel, t.Language })
            .ToListAsync(cancellationToken);

        return keys.Select(k => new NotificationTemplateKey(k.Type, k.Channel, k.Language)).ToHashSet();
    }

    public async Task<IReadOnlyList<NotificationTemplateSummaryRow>> ListAsync(
        NotificationTemplateFilter filter, CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationTemplates.AsNoTracking().AsQueryable();
        if (filter.Category is { } category)
        {
            query = query.Where(t => t.Category == category);
        }

        if (filter.Channel is { } channel)
        {
            query = query.Where(t => t.Channel == channel);
        }

        if (filter.Language is { } language)
        {
            query = query.Where(t => t.Language == language);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(t => t.Type == type);
        }

        return await query
            .OrderBy(t => t.Type).ThenBy(t => t.Channel).ThenBy(t => t.Language)
            .Select(t => new NotificationTemplateSummaryRow
            {
                TemplateId = t.Id,
                Type = t.Type,
                Category = t.Category,
                Channel = t.Channel,
                Language = t.Language,
                Name = t.Name,
                PublishedVersionId = t.PublishedVersionId,
                PublishedVersionNumber = t.Versions.Where(v => v.Status == TemplateVersionStatus.Published).Select(v => (int?)v.VersionNumber).FirstOrDefault(),
                PublishedAtUtc = t.Versions.Where(v => v.Status == TemplateVersionStatus.Published).Select(v => v.PublishedAtUtc).FirstOrDefault(),
                HasDraft = t.Versions.Any(v => v.Status == TemplateVersionStatus.Draft),
            })
            .ToListAsync(cancellationToken);
    }

    public Task<NotificationTemplate?> GetWithVersionsAsync(Guid templateId, bool track, CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationTemplates.Include(t => t.Versions).AsQueryable();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken);
    }

    public async Task<NotificationTemplateVersionLookup?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.NotificationTemplates
            .AsNoTracking()
            .SelectMany(t => t.Versions)
            .SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var owner = await dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Id == version.TemplateId)
            .Select(t => new { t.Type, t.Language })
            .SingleAsync(cancellationToken);
        return new NotificationTemplateVersionLookup(version, owner.Type, owner.Language);
    }

    public void ExpectRowVersion(NotificationTemplateVersion version, byte[] rowVersion) =>
        dbContext.Entry(version).Property(v => v.RowVersion).OriginalValue = rowVersion;

    public void Add(NotificationTemplate notificationTemplate) => dbContext.NotificationTemplates.Add(notificationTemplate);
}
