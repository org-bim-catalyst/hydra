using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// A user's personal AI defaults (FR-017/FR-019) — created lazily on first save, not at
/// registration (data-model.md).
/// </summary>
public sealed class UserAiPreference : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public Guid? DefaultProviderId { get; private set; }

    public Guid? DefaultModelId { get; private set; }

    public string? DefaultGenerationParametersJson { get; private set; }

    private UserAiPreference()
    {
        // Required by EF Core materialization.
    }

    public static UserAiPreference Create(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A preference must belong to a user.");
        }

        return new UserAiPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-017/FR-019. Cross-field validity (model belongs to provider) is an Application-layer concern (data-model.md).</summary>
    public void SetDefaults(Guid? providerId, Guid? modelId, string? generationParametersJson, string actor)
    {
        DefaultProviderId = providerId;
        DefaultModelId = modelId;
        DefaultGenerationParametersJson = generationParametersJson;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
