using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

public enum PromptAuditAction
{
    Created,
    Updated,
    Deleted,
    Archived,
    Restored,
    Duplicated,
    VersionRestored,
    Exported,
    Imported,
}

/// <summary>
/// An immutable record of security- and lifecycle-relevant actions on a <see cref="Prompt"/>
/// (spec.md FR-090). No cascade FK to <see cref="Prompt"/> — must survive a hard-purged prompt,
/// mirroring <c>KnowledgeBaseAuditLog</c>/<c>MemoryAuditLog</c>. <see cref="DetailsJson"/> is always
/// sanitized — never raw prompt content (FR-091).
/// </summary>
public sealed class PromptAuditLog : BaseEntity
{
    public Guid PromptId { get; private set; }

    public PromptAuditAction Action { get; private set; }

    public string ActorId { get; private set; } = string.Empty;

    public string? DetailsJson { get; private set; }

    private PromptAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static PromptAuditLog Create(Guid promptId, PromptAuditAction action, string actorId, string? detailsJson)
    {
        return new PromptAuditLog
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            Action = action,
            ActorId = actorId,
            DetailsJson = detailsJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorId,
        };
    }
}
