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

    public void Add(NotificationTemplate template) => dbContext.NotificationTemplates.Add(template);
}
