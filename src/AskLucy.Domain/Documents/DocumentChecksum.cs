using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>The content hash used for duplicate detection and integrity (FR-009, research.md Decision 8). Referenced 1:1 by exactly one <see cref="DocumentVersion"/>.</summary>
public sealed class DocumentChecksum : BaseEntity
{
    /// <summary>Fixed to "SHA-256" for now (research.md Decision 8).</summary>
    public string Algorithm { get; private set; } = "SHA-256";

    /// <summary>64 hex chars for SHA-256.</summary>
    public string Hash { get; private set; } = string.Empty;

    private DocumentChecksum()
    {
        // Required by EF Core materialization.
    }

    public static DocumentChecksum Create(string hash, string actor)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new DomainRuleViolationException("A checksum hash is required.");
        }

        return new DocumentChecksum
        {
            Id = Guid.CreateVersion7(),
            Hash = hash,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
