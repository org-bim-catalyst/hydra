using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>Whether an embedding provider runs in the cloud or entirely in-process/self-hosted (spec.md FR-009a, research.md Decision 5).</summary>
public enum EmbeddingHostingType
{
    Cloud,
    Local,
}

/// <summary>
/// A configured embedding source available to knowledge bases (spec.md FR-006, FR-009a,
/// data-model.md). Knowledge bases requiring data residency are restricted (at the Application
/// layer and via <see cref="KnowledgeBases.KnowledgeBase.UpdateRetrievalSettings"/>'s own
/// invariant) to <see cref="EmbeddingHostingType.Local"/> providers only.
/// </summary>
public sealed class EmbeddingProvider : BaseEntity
{
    public string Vendor { get; private set; } = string.Empty;

    public string ModelKey { get; private set; } = string.Empty;

    public int Dimensionality { get; private set; }

    public EmbeddingHostingType HostingType { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; } = true;

    private EmbeddingProvider()
    {
        // Required by EF Core materialization.
    }

    public static EmbeddingProvider Create(string vendor, string modelKey, int dimensionality, EmbeddingHostingType hostingType, bool isDefault, string actor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new DomainRuleViolationException("An embedding provider must have a vendor.");
        }

        if (string.IsNullOrWhiteSpace(modelKey))
        {
            throw new DomainRuleViolationException("An embedding provider must have a model key.");
        }

        if (dimensionality <= 0)
        {
            throw new DomainRuleViolationException("An embedding provider must have a positive dimensionality.");
        }

        return new EmbeddingProvider
        {
            Id = Guid.CreateVersion7(),
            Vendor = vendor.Trim(),
            ModelKey = modelKey.Trim(),
            Dimensionality = dimensionality,
            HostingType = hostingType,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Deactivate(string actor)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
