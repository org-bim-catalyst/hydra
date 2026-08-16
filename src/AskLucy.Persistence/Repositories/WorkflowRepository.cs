using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class WorkflowRepository(AskLucyDbContext dbContext) : IWorkflowRepository
{
    private const int MaxPageSize = 200;

    public Task<Workflow?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) =>
        dbContext.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == ownerId, cancellationToken);

    public Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Workflows.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public void Add(Workflow workflow) => dbContext.Workflows.Add(workflow);

    public Task<bool> ExistsWithNameForOwnerAsync(string ownerId, string name, Guid? excludingWorkflowId, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Workflows.Where(w => w.OwnerId == ownerId && w.Name == name);
        if (excludingWorkflowId is { } excluded)
        {
            query = query.Where(w => w.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Workflow> Items, string? NextCursor)> ListByOwnerAsync(
        string ownerId, WorkflowStatus? status, WorkflowType? workflowType, string? search, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.Workflows.Where(w => w.OwnerId == ownerId);

        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }

        if (workflowType is not null)
        {
            query = query.Where(w => w.WorkflowType == workflowType);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(w => EF.Functions.Like(w.Name, $"%{search}%"));
        }

        var decoded = WorkflowCursor.Decode(cursor);
        var sorted = query
            .Select(w => new { Workflow = w, SortValue = w.ModifiedAtUtc ?? w.CreatedAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Workflow.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Workflow.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Workflow.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = WorkflowCursor.Encode(last.SortValue, last.Workflow.Id);
        }

        return (items.Select(x => x.Workflow).ToList(), nextCursor);
    }

    public Task<WorkflowVersion?> GetVersionAsync(Guid workflowId, int versionNumber, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowVersions
            .Include(v => v.Nodes)
            .Include(v => v.Connections)
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.WorkflowId == workflowId && v.VersionNumber == versionNumber, cancellationToken);

    public Task<WorkflowVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowVersions
            .Include(v => v.Nodes)
            .Include(v => v.Connections)
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowVersion>> ListVersionsAsync(Guid workflowId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(Workflow Workflow, WorkflowVersion Version)>> ListPublishedEventDrivenAsync(CancellationToken cancellationToken = default)
    {
        var workflows = await dbContext.Workflows
            .Where(w => w.Status == WorkflowStatus.Published && w.WorkflowType == WorkflowType.EventDriven && w.EventTriggerConfigurationJson != null)
            .ToListAsync(cancellationToken);

        var results = new List<(Workflow, WorkflowVersion)>();
        foreach (var workflow in workflows)
        {
            if (workflow.PublishedVersionNumber is not { } versionNumber)
            {
                continue;
            }

            var version = await GetVersionAsync(workflow.Id, versionNumber, cancellationToken);
            if (version is not null)
            {
                results.Add((workflow, version));
            }
        }

        return results;
    }
}
