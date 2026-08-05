using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// The classification taxonomy lookup (FR-025/FR-026's extensibility assumption, data-model.md),
/// mirroring <c>KnowledgeBaseCategory</c>'s existing convention (specs/014). Seeded with the
/// starting taxonomy (<see cref="IsSystemDefined"/> = true); administrators can add more without
/// a pipeline redesign.
/// </summary>
public sealed class DocumentCategory : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public bool IsSystemDefined { get; private set; }

    private DocumentCategory()
    {
        // Required by EF Core materialization.
    }

    public static DocumentCategory Create(string name, bool isSystemDefined, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A category name is required.");
        }

        return new DocumentCategory
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            IsSystemDefined = isSystemDefined,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
