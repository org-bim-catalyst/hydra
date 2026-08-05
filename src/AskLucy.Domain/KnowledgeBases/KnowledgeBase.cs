using AskLucy.Domain.Common;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>Lifecycle status (FR-002). "Deleted" is deliberately not a value here — it is the
/// orthogonal <see cref="BaseEntity.DeletedAtUtc"/> soft-delete flag, mirroring <c>UserChat</c>
/// (research.md Decision 2): restoring a soft-deleted knowledge base needs no "what status was
/// I in before" lookup.</summary>
public enum KnowledgeBaseStatus
{
    Draft,
    Active,
    Archived,
}

/// <summary>Only <see cref="Private"/> is reachable in this release (FR-009); the field exists
/// now so team/organization/public sharing can be added later without a breaking schema
/// change (spec.md Assumptions).</summary>
public enum KnowledgeBaseVisibility
{
    Private,
}

/// <summary>A knowledge base's RAG index status (spec.md FR-014, research.md Decision 11) — an independent axis from <see cref="KnowledgeBaseStatus"/>.</summary>
public enum KnowledgeBaseIndexStatus
{
    NotIndexed,
    InitialIndexQueued,
    Indexing,
    PartiallyIndexed,
    Indexed,
    Failed,
}

/// <summary>Which backing store holds this knowledge base's vectors (ADR-0007). <see cref="Pinecone"/>
/// is the default for new knowledge bases; <see cref="SqlServer"/> remains available and is
/// mandatory when <see cref="KnowledgeBase.RequiresDataResidency"/> is set, since Pinecone is a
/// third-party US-hosted SaaS.</summary>
public enum VectorStoreProvider
{
    SqlServer,
    Pinecone,
}

/// <summary>
/// A private, user-owned container grouping related documents for a purpose (spec.md Key
/// Entities). Mirrors <c>UserChat</c>'s lifecycle shape (plan.md Summary): status enum +
/// independent Favorite/Pinned flags + <see cref="BaseEntity"/> soft delete, rather than a
/// single all-encompassing state enum. Owns <see cref="KnowledgeBaseTag"/> assignments;
/// folders/documents reference this aggregate by id but are not loaded as navigation
/// collections here (they have their own independent query/repository needs — data-model.md).
/// </summary>
public sealed class KnowledgeBase : BaseEntity
{
    private readonly List<KnowledgeBaseTag> _tags = [];

    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public KnowledgeBaseStatus Status { get; private set; }

    public KnowledgeBaseVisibility Visibility { get; private set; } = KnowledgeBaseVisibility.Private;

    public string? Color { get; private set; }

    public string? Icon { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string? Notes { get; private set; }

    public bool IsFavorite { get; private set; }

    /// <summary>Non-null = pinned; also the sort key for pinned-first ordering (FR-028), mirrors <c>UserChat.PinnedAtUtc</c>.</summary>
    public DateTime? PinnedAtUtc { get; private set; }

    /// <summary>Denormalized cached counter (FR-030/FR-031/FR-035) — see data-model.md "Explicitly Not Modeled" for why there is no separate statistics table.</summary>
    public int DocumentCount { get; private set; }

    public int TotalPageCount { get; private set; }

    public long StorageSizeBytes { get; private set; }

    /// <summary>Set to <c>DeletedAtUtc + 30 days</c> on soft delete (FR-036); cleared on <see cref="Restore"/>. Read by the periodic purge sweep.</summary>
    public DateTime? PurgeScheduledAtUtc { get; private set; }

    /// <summary>RAG chunking strategy (spec.md FR-001, research.md Decision 11). Defaults to <see cref="ChunkingStrategy.Recursive"/> at creation.</summary>
    public ChunkingStrategy ChunkingStrategy { get; private set; } = ChunkingStrategy.Recursive;

    /// <summary>The <c>EmbeddingProvider</c> this knowledge base uses to generate embeddings (FR-006). Null = the platform's default cloud provider.</summary>
    public Guid? EmbeddingProviderId { get; private set; }

    /// <summary>FR-009a (spec.md Clarifications Q1) — when true, <see cref="EmbeddingProviderId"/> must resolve to a <see cref="EmbeddingHostingType.Local"/> provider, and <see cref="VectorStoreProvider"/> must be <see cref="VectorStoreProvider.SqlServer"/>.</summary>
    public bool RequiresDataResidency { get; private set; }

    /// <summary>ADR-0007 — defaults to <see cref="VectorStoreProvider.Pinecone"/> for new knowledge bases; existing rows are backfilled to <see cref="VectorStoreProvider.SqlServer"/> by the migration since their vectors already live there.</summary>
    public VectorStoreProvider VectorStoreProvider { get; private set; } = VectorStoreProvider.Pinecone;

    public KnowledgeBaseIndexStatus IndexStatus { get; private set; } = KnowledgeBaseIndexStatus.NotIndexed;

    public DateTime? LastIndexedAtUtc { get; private set; }

    public IReadOnlyCollection<KnowledgeBaseTag> Tags => _tags;

    private KnowledgeBase()
    {
        // Required by EF Core materialization.
    }

    public static KnowledgeBase Create(string name, string ownerId, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A knowledge base name is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A knowledge base must belong to a user.");
        }

        return new KnowledgeBase
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            Status = KnowledgeBaseStatus.Draft,
            Visibility = KnowledgeBaseVisibility.Private,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>
    /// Full-replace update of every editable field (FR-003) — the caller (frontend edit
    /// form/API request) always sends the complete desired state, same convention as
    /// <c>SaveUserAiPreferenceCommand</c>/<c>SaveUserVoicePreferenceCommand</c>. <paramref
    /// name="description"/>/<paramref name="color"/>/<paramref name="icon"/>/<paramref
    /// name="categoryId"/>/<paramref name="notes"/> are nullable because null is itself a
    /// meaningful value (e.g., "no category") — a true field-level PATCH is not needed here.
    /// Tags are managed separately via <see cref="AddTag"/>/<see cref="RemoveTag"/>.
    /// </summary>
    public void UpdateDetails(string name, string? description, string? color, string? icon, Guid? categoryId, string? notes, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A knowledge base name is required.");
        }

        Name = name.Trim();
        Description = description;
        Color = color;
        Icon = icon;
        CategoryId = categoryId;
        Notes = notes;

        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void ClearCategory(string actor)
    {
        if (CategoryId is null)
        {
            return;
        }

        CategoryId = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public KnowledgeBaseTag AddTag(string value, string ownerId, string actor)
    {
        if (_tags.Any(t => string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return _tags.First(t => string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase));
        }

        var tag = KnowledgeBaseTag.Create(Id, ownerId, value, actor);
        _tags.Add(tag);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
        return tag;
    }

    public void RemoveTag(KnowledgeBaseTag tag, string actor)
    {
        _tags.Remove(tag);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Draft -> Active (research.md Decision 1). Required before future RAG indexing eligibility (FR-006).</summary>
    public void Activate(string actor)
    {
        if (Status != KnowledgeBaseStatus.Draft)
        {
            throw new DomainRuleViolationException("Only a Draft knowledge base can be activated.");
        }

        Status = KnowledgeBaseStatus.Active;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Archive(string actor)
    {
        if (Status != KnowledgeBaseStatus.Active)
        {
            throw new DomainRuleViolationException("Only an Active knowledge base can be archived.");
        }

        Status = KnowledgeBaseStatus.Archived;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Un-archives back to Active, or (if soft-deleted) cancels the pending purge and returns to whatever <see cref="Status"/> already holds — never touched by <see cref="SoftDelete"/> (research.md Decision 2).</summary>
    public void Restore(string actor)
    {
        if (DeletedAtUtc is not null)
        {
            DeletedAtUtc = null;
            DeletedBy = null;
            PurgeScheduledAtUtc = null;
        }
        else if (Status == KnowledgeBaseStatus.Archived)
        {
            Status = KnowledgeBaseStatus.Active;
        }
        else
        {
            throw new DomainRuleViolationException("This knowledge base is not archived or soft-deleted.");
        }

        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Soft delete (FR-005) — schedules the automatic 30-day purge (FR-036).</summary>
    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
        PurgeScheduledAtUtc = DeletedAtUtc.Value.AddDays(30);
    }

    public void MarkFavorite(string actor)
    {
        if (IsFavorite)
        {
            return;
        }

        IsFavorite = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void UnmarkFavorite(string actor)
    {
        if (!IsFavorite)
        {
            return;
        }

        IsFavorite = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Pin(string actor)
    {
        PinnedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Unpin(string actor)
    {
        if (PinnedAtUtc is null)
        {
            return;
        }

        PinnedAtUtc = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Called when a document is added to this knowledge base (FR-030/FR-031) — keeps the cached statistics columns authoritative without a join on every dashboard read.</summary>
    public void ApplyDocumentAdded(int? pageCount, long sizeBytes, string actor)
    {
        DocumentCount++;
        TotalPageCount += pageCount ?? 0;
        StorageSizeBytes += sizeBytes;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void ApplyDocumentRemoved(int? pageCount, long sizeBytes, string actor)
    {
        DocumentCount = Math.Max(0, DocumentCount - 1);
        TotalPageCount = Math.Max(0, TotalPageCount - (pageCount ?? 0));
        StorageSizeBytes = Math.Max(0, StorageSizeBytes - sizeBytes);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public bool IsOwnedBy(string userId) => OwnerId == userId;

    /// <summary>
    /// FR-001, FR-004, FR-009a, research.md Decision 11, ADR-0007 — <paramref name="resolvedProviderHostingType"/>
    /// is the effective <see cref="EmbeddingProvider"/>'s hosting type that <paramref name="embeddingProviderId"/>
    /// resolves to (the caller/Application layer always resolves this — including the "null means the
    /// platform default cloud provider" case — before calling, so Domain enforces the invariant against
    /// a real value rather than trusting the caller). Both the embedding-hosting guard and the
    /// vector-store guard are validated together here, in one method, because
    /// <see cref="RequiresDataResidency"/> is a single cross-cutting invariant spanning both — splitting
    /// this into two methods would let a caller flip <paramref name="requiresDataResidency"/> to true via
    /// one call while leaving a stale non-compliant choice set by a prior call to the other. Returns
    /// <see langword="true"/> when the chunking strategy, embedding provider, or vector store provider
    /// actually changed, so the caller knows whether FR-004's automatic reindex trigger applies.
    /// </summary>
    public bool UpdateRetrievalSettings(
        ChunkingStrategy chunkingStrategy, Guid? embeddingProviderId, EmbeddingHostingType resolvedProviderHostingType,
        VectorStoreProvider vectorStoreProvider, bool requiresDataResidency, string actor)
    {
        if (requiresDataResidency && resolvedProviderHostingType != EmbeddingHostingType.Local)
        {
            throw new DomainRuleViolationException("A knowledge base requiring data residency must use a local/self-hosted embedding provider.");
        }

        if (requiresDataResidency && vectorStoreProvider != VectorStoreProvider.SqlServer)
        {
            throw new DomainRuleViolationException("A knowledge base requiring data residency must use SQL Server for vector storage.");
        }

        var changed = ChunkingStrategy != chunkingStrategy || EmbeddingProviderId != embeddingProviderId || VectorStoreProvider != vectorStoreProvider;

        ChunkingStrategy = chunkingStrategy;
        EmbeddingProviderId = embeddingProviderId;
        VectorStoreProvider = vectorStoreProvider;
        RequiresDataResidency = requiresDataResidency;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;

        return changed;
    }

    /// <summary>FR-010a, FR-011 — requires <see cref="Status"/> to be <see cref="KnowledgeBaseStatus.Active"/> (RAG indexing eligibility) and <see cref="IndexStatus"/> to not already be in progress (§5 Concurrency).</summary>
    public void MarkInitialIndexQueued(string actor)
    {
        if (Status != KnowledgeBaseStatus.Active)
        {
            throw new DomainRuleViolationException("Only an Active knowledge base can be indexed.");
        }

        if (IndexStatus is KnowledgeBaseIndexStatus.InitialIndexQueued or KnowledgeBaseIndexStatus.Indexing)
        {
            throw new DomainRuleViolationException("This knowledge base already has an indexing job in progress.");
        }

        IndexStatus = KnowledgeBaseIndexStatus.InitialIndexQueued;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-011 — a reindex on an already-indexed knowledge base; same concurrency guard as <see cref="MarkInitialIndexQueued"/>.</summary>
    public void MarkReindexQueued(string actor)
    {
        if (IndexStatus is KnowledgeBaseIndexStatus.InitialIndexQueued or KnowledgeBaseIndexStatus.Indexing)
        {
            throw new DomainRuleViolationException("This knowledge base already has an indexing job in progress.");
        }

        IndexStatus = KnowledgeBaseIndexStatus.Indexing;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkIndexing(string actor)
    {
        IndexStatus = KnowledgeBaseIndexStatus.Indexing;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkIndexed(bool partial, string actor)
    {
        IndexStatus = partial ? KnowledgeBaseIndexStatus.PartiallyIndexed : KnowledgeBaseIndexStatus.Indexed;
        LastIndexedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkIndexFailed(string actor)
    {
        IndexStatus = KnowledgeBaseIndexStatus.Failed;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
