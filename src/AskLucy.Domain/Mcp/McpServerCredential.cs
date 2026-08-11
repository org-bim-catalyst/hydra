using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

/// <summary>
/// Server-side-only credential material for an <see cref="McpServer"/> (spec.md FR-045-FR-047,
/// research.md Decision 7). <see cref="CiphertextBlob"/> is always already encrypted by the time
/// it reaches the Domain layer — this entity never sees or stores a plaintext credential.
/// </summary>
public sealed class McpServerCredential : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public string CiphertextBlob { get; private set; } = string.Empty;

    public DateTime RotatedAtUtc { get; private set; }

    public string RotatedByUserId { get; private set; } = string.Empty;

    private McpServerCredential()
    {
        // Required by EF Core materialization.
    }

    public static McpServerCredential Create(Guid mcpServerId, string ciphertextBlob, string actor)
    {
        if (string.IsNullOrWhiteSpace(ciphertextBlob))
        {
            throw new DomainRuleViolationException("A credential ciphertext is required.");
        }

        var now = DateTime.UtcNow;
        return new McpServerCredential
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            CiphertextBlob = ciphertextBlob,
            RotatedAtUtc = now,
            RotatedByUserId = actor,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    public void Rotate(string ciphertextBlob, string actor)
    {
        if (string.IsNullOrWhiteSpace(ciphertextBlob))
        {
            throw new DomainRuleViolationException("A credential ciphertext is required.");
        }

        CiphertextBlob = ciphertextBlob;
        RotatedAtUtc = DateTime.UtcNow;
        RotatedByUserId = actor;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
