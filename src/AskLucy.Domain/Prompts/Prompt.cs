using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

public enum PromptType
{
    Chat,
    System,
    Instruction,
    Summarization,
    Translation,
    Extraction,
    Classification,
    Rag,
    StructuredOutput,
}

public enum PromptStatus
{
    Draft,
    Active,
    Archived,
}

/// <summary>Required-capability flags a model must satisfy to execute this prompt (spec.md FR-004) — assembled into the existing <c>AskLucy.Domain.Ai.AIModelCapabilities</c> shape by Application-layer code for the actual comparison; stored here as flat columns, mirroring how <c>AIModel</c> itself stores its own capabilities (data-model.md).</summary>
public sealed record PromptCapabilityRequirements(
    bool RequiresStreaming,
    bool RequiresVision,
    bool RequiresFunctionCalling,
    bool RequiresJsonMode,
    bool RequiresReasoning,
    bool RequiresEmbeddings,
    bool RequiresImageInput,
    bool RequiresImageOutput,
    bool RequiresAudio)
{
    public static readonly PromptCapabilityRequirements None = new(false, false, false, false, false, false, false, false, false);
}

/// <summary>
/// The reusable prompt asset (spec.md FR-001-FR-007, data-model.md). Aggregate root for the
/// <c>Prompts</c> bounded context — owns its <see cref="PromptVersion"/> history and
/// <see cref="PromptTag"/> assignments. Content/variable/model-setting fields on this entity are a
/// denormalized copy of the current version's content (kept in sync by <see cref="ApplyEdit"/>) so
/// full-text search (research.md Decision 12) can index the <c>Prompts</c> table directly without a
/// join to <c>PromptVersions</c>.
/// </summary>
public sealed class Prompt : BaseEntity
{
    private readonly List<PromptTag> _tags = [];
    private readonly List<PromptVersion> _versions = [];

    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public PromptType PromptType { get; private set; }

    public PromptStatus Status { get; private set; }

    public string? SystemInstructions { get; private set; }

    public string? DeveloperInstructions { get; private set; }

    public string UserInstructions { get; private set; } = string.Empty;

    public string? ContextText { get; private set; }

    public string? ExamplesText { get; private set; }

    public string? OutputInstructions { get; private set; }

    public string? Constraints { get; private set; }

    public Guid? FolderId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid CurrentVersionId { get; private set; }

    public int CurrentVersionNumber { get; private set; }

    public bool IsFavorite { get; private set; }

    public bool IsPinned { get; private set; }

    public PromptCapabilityRequirements RequiredCapabilities { get; private set; } = PromptCapabilityRequirements.None;

    public string? PreferredModelKey { get; private set; }

    public IReadOnlyCollection<PromptTag> Tags => _tags;

    public IReadOnlyCollection<PromptVersion> Versions => _versions;

    private Prompt()
    {
        // Required by EF Core materialization.
    }

    public static (Prompt Prompt, PromptVersion Version) Create(
        string ownerId,
        string name,
        string? description,
        PromptType promptType,
        Guid? folderId,
        Guid? categoryId,
        PromptCapabilityRequirements requiredCapabilities,
        string? preferredModelKey,
        PromptContentSnapshot content,
        IReadOnlyList<PromptVariableDefinition> variables,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A prompt must have an owner.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A prompt name is required.");
        }

        var now = DateTime.UtcNow;
        var prompt = new Prompt
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            Description = description,
            PromptType = promptType,
            Status = PromptStatus.Active,
            FolderId = folderId,
            CategoryId = categoryId,
            RequiredCapabilities = requiredCapabilities,
            PreferredModelKey = preferredModelKey,
            CurrentVersionNumber = 1,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };

        var version = PromptVersion.Create(prompt.Id, 1, content, variables, changeDescription: null, actor);
        prompt._versions.Add(version);
        prompt.CurrentVersionId = version.Id;
        prompt.ApplyContentSnapshot(content);

        return (prompt, version);
    }

    /// <summary>Content/variable/model-setting edit (spec.md FR-030) — always creates a new <see cref="PromptVersion"/>. Organizational metadata (name/folder/category/favorite/pinned) is changed via the dedicated methods below instead, none of which version the prompt.</summary>
    public PromptVersion ApplyEdit(PromptContentSnapshot content, IReadOnlyList<PromptVariableDefinition> variables, string? changeDescription, string actor)
    {
        var nextVersionNumber = CurrentVersionNumber + 1;
        var version = PromptVersion.Create(Id, nextVersionNumber, content, variables, changeDescription, actor);
        _versions.Add(version);

        CurrentVersionId = version.Id;
        CurrentVersionNumber = nextVersionNumber;
        ApplyContentSnapshot(content);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;

        return version;
    }

    /// <summary>Restores a prior version's content as the new current state (spec.md FR-033) — creates a brand-new version copying the restored content rather than mutating or deleting any existing version.</summary>
    public PromptVersion RestoreFrom(PromptVersion version, string actor)
    {
        if (version.PromptId != Id)
        {
            throw new DomainRuleViolationException("Cannot restore a version that does not belong to this prompt.");
        }

        var content = new PromptContentSnapshot(
            version.SystemInstructions, version.DeveloperInstructions, version.UserInstructions,
            version.ContextText, version.ExamplesText, version.OutputInstructions, version.Constraints,
            version.ProviderKey, version.ModelKey, version.Temperature, version.MaxOutputTokens,
            version.StructuredOutputRequested);

        var variables = version.Variables
            .OrderBy(v => v.OrderIndex)
            .Select(v => new PromptVariableDefinition(
                v.Name, v.Description, v.VariableType, v.IsRequired, v.DefaultValue, v.ExampleValue,
                v.ValidationRulesJson, v.OrderIndex))
            .ToList();

        var changeDescription = $"Restored from version {version.VersionNumber}.";
        return ApplyEdit(content, variables, changeDescription, actor);
    }

    public void Rename(string name, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A prompt name is required.");
        }

        Name = name.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetFolder(Guid? folderId, string actor)
    {
        FolderId = folderId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetCategory(Guid? categoryId, string actor)
    {
        CategoryId = categoryId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetFavorite(bool isFavorite, string actor)
    {
        IsFavorite = isFavorite;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetPinned(bool isPinned, string actor)
    {
        IsPinned = isPinned;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Archive(string actor)
    {
        Status = PromptStatus.Archived;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Restore(string actor)
    {
        Status = PromptStatus.Active;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    public PromptTag AddTag(string value, string ownerId, string actor)
    {
        var tag = PromptTag.Create(Id, ownerId, value, actor);
        _tags.Add(tag);
        return tag;
    }

    public void RemoveTag(Guid tagId, string actor)
    {
        var tag = _tags.FirstOrDefault(t => t.Id == tagId);
        tag?.SoftDelete(actor);
    }

    private void ApplyContentSnapshot(PromptContentSnapshot content)
    {
        SystemInstructions = content.SystemInstructions;
        DeveloperInstructions = content.DeveloperInstructions;
        UserInstructions = content.UserInstructions.Trim();
        ContextText = content.ContextText;
        ExamplesText = content.ExamplesText;
        OutputInstructions = content.OutputInstructions;
        Constraints = content.Constraints;
    }
}
