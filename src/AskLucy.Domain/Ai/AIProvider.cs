using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

public enum ProviderHealthStatus
{
    Unknown,
    Healthy,
    Unhealthy,
}

/// <summary>
/// One AI vendor the platform can call (OpenAI, Anthropic, Google Gemini, OpenRouter, or a
/// future vendor) — data-model.md. Seeded at migration time; administrators enable it and
/// configure its credential (FR-003/FR-004) before it becomes selectable to users (FR-007).
/// </summary>
public sealed class AIProvider : BaseEntity
{
    public string ProviderKey { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    /// <summary>Data-Protection-encrypted API key (research.md Decision 4). Never serialized into any DTO.</summary>
    public string? CredentialCiphertext { get; private set; }

    public DateTime? CredentialLastRotatedAtUtc { get; private set; }

    public Guid? DefaultModelId { get; private set; }

    /// <summary>Denormalized "latest known" status for fast reads (FR-027); the authoritative history is <see cref="ProviderHealthCheck"/>.</summary>
    public ProviderHealthStatus HealthStatus { get; private set; } = ProviderHealthStatus.Unknown;

    public DateTime? HealthStatusCheckedAtUtc { get; private set; }

    private AIProvider()
    {
        // Required by EF Core materialization.
    }

    public static AIProvider Create(string providerKey, string displayName, string actor)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new DomainRuleViolationException("A provider key is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainRuleViolationException("A display name is required.");
        }

        return new AIProvider
        {
            Id = Guid.CreateVersion7(),
            ProviderKey = providerKey.Trim(),
            DisplayName = displayName.Trim(),
            IsEnabled = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-003. Throws if no credential has been configured yet (FR-004) — a provider cannot go live with nothing to authenticate with.</summary>
    public void Enable(string actor)
    {
        if (string.IsNullOrEmpty(CredentialCiphertext))
        {
            throw new DomainRuleViolationException("Cannot enable a provider with no credential configured.");
        }

        IsEnabled = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Disable(string actor)
    {
        IsEnabled = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-004. The plaintext key is encrypted by the caller (Infrastructure) before reaching this method — Domain never sees it.</summary>
    public void SetCredential(string ciphertext, string actor)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new DomainRuleViolationException("A credential is required.");
        }

        CredentialCiphertext = ciphertext;
        CredentialLastRotatedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Clearing a credential also disables the provider (contracts/admin.md) — it cannot stay enabled with nothing to authenticate with.</summary>
    public void ClearCredential(string actor)
    {
        CredentialCiphertext = null;
        CredentialLastRotatedAtUtc = null;
        IsEnabled = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetDefaultModel(Guid? modelId, string actor)
    {
        DefaultModelId = modelId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>
    /// Updates the denormalized health snapshot only — deliberately does not touch
    /// ModifiedAtUtc/ModifiedBy, which track administrator-driven configuration changes,
    /// not automated health-check pings (data-model.md).
    /// </summary>
    public void UpdateHealthStatus(bool isHealthy, DateTime checkedAtUtc)
    {
        HealthStatus = isHealthy ? ProviderHealthStatus.Healthy : ProviderHealthStatus.Unhealthy;
        HealthStatusCheckedAtUtc = checkedAtUtc;
    }
}
