namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>A user an incident or occurrence refers to. <see cref="IsDeleted"/> is the soft-deleted account.</summary>
public sealed record UserReference(string Id, string? DisplayName, string? Email, bool IsDeleted);

/// <summary>A chat, workflow, document, agent or MCP server an incident or occurrence refers to.</summary>
public sealed record ItemReference(Guid Id, string? Label, bool IsDeleted);

public enum ReferencedItemKind
{
    Chat,
    Workflow,
    Document,
    Agent,
    McpServer,
}

/// <summary>
/// Batch label resolution for the admin trail (FR-016): references are stored as ids and resolved
/// at read time, so a rename shows its current name. Deleted items are still found — the trail
/// links to them as "deleted" rather than dropping them. An id that resolves to nothing is simply
/// absent from the result.
/// </summary>
public interface IOperationalFailureReferenceLookup
{
    Task<IReadOnlyDictionary<string, UserReference>> FindUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, ItemReference>> FindItemsAsync(
        ReferencedItemKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
