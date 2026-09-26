using AskLucy.Application.OperationalFailures.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/074 FR-016. Ignores the soft-delete query filters on purpose: the trail links a deleted
/// user or item as "deleted" instead of losing it. Labels only — no content is read here.
/// </summary>
public sealed class OperationalFailureReferenceLookup(AskLucyDbContext dbContext) : IOperationalFailureReferenceLookup
{
    public async Task<IReadOnlyDictionary<string, UserReference>> FindUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserReference>(StringComparer.Ordinal);
        }

        var users = await dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.IsDeleted })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => new UserReference(u.Id, DisplayName(u.FirstName, u.LastName) ?? u.Email, u.Email, u.IsDeleted),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<Guid, ItemReference>> FindItemsAsync(
        ReferencedItemKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, ItemReference>();
        }

        var items = kind switch
        {
            ReferencedItemKind.Chat => await dbContext.UserChats.IgnoreQueryFilters().AsNoTracking()
                .Where(c => distinct.Contains(c.Id))
                .Select(c => new ItemReference(c.Id, c.Title, c.DeletedAtUtc != null))
                .ToListAsync(cancellationToken),
            ReferencedItemKind.Workflow => await dbContext.Workflows.IgnoreQueryFilters().AsNoTracking()
                .Where(w => distinct.Contains(w.Id))
                .Select(w => new ItemReference(w.Id, w.Name, w.DeletedAtUtc != null))
                .ToListAsync(cancellationToken),
            ReferencedItemKind.Document => await dbContext.Documents.IgnoreQueryFilters().AsNoTracking()
                .Where(d => distinct.Contains(d.Id))
                .Select(d => new ItemReference(d.Id, d.FileName, d.DeletedAtUtc != null))
                .ToListAsync(cancellationToken),
            ReferencedItemKind.Agent => await dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
                .Where(a => distinct.Contains(a.Id))
                .Select(a => new ItemReference(a.Id, a.Name, a.DeletedAtUtc != null))
                .ToListAsync(cancellationToken),
            ReferencedItemKind.McpServer => await dbContext.McpServers.IgnoreQueryFilters().AsNoTracking()
                .Where(s => distinct.Contains(s.Id))
                .Select(s => new ItemReference(s.Id, s.Name, s.DeletedAtUtc != null))
                .ToListAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return items.ToDictionary(i => i.Id);
    }

    private static string? DisplayName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
