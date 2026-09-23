using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// One text-to-speech engine the platform can speak through (ElevenLabs, Supertonic, or a
/// future engine) — specs/070 data-model.md. Deliberately separate from <see cref="AIProvider"/>:
/// a voice engine is not a chat-completion vendor, has no model catalog or health-check history,
/// and is ordered (<see cref="Priority"/>) rather than enabled/disabled — the lowest priority is
/// Lucy's voice, every other row is a failover in ascending order.
/// </summary>
public sealed class VoiceProvider : BaseEntity
{
    public const int MaxVoiceIdLength = 100;

    /// <summary>Matches the <c>ProviderKey</c> of a registered text-to-speech engine implementation.</summary>
    public string ProviderKey { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>0 is Lucy's voice; higher values are tried in ascending order when every lower one fails.</summary>
    public int Priority { get; private set; }

    /// <summary>The voice this engine speaks with by default. Null means "the engine's own configured default".</summary>
    public string? DefaultVoiceId { get; private set; }

    /// <summary>Data-Protection-encrypted API key, same scheme as <see cref="AIProvider.CredentialCiphertext"/>. Never serialized into any DTO. Null for a local engine, or for one still using its configuration-file key.</summary>
    public string? CredentialCiphertext { get; private set; }

    /// <summary>Vendor-style fingerprint of the plaintext key (specs/066) — safe to return to the client.</summary>
    public string? CredentialHint { get; private set; }

    public DateTime? CredentialLastRotatedAtUtc { get; private set; }

    private VoiceProvider()
    {
        // Required by EF Core materialization.
    }

    public static VoiceProvider Create(string providerKey, string displayName, int priority, string actor)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new DomainRuleViolationException("A provider key is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainRuleViolationException("A display name is required.");
        }

        if (priority < 0)
        {
            throw new DomainRuleViolationException("A voice provider's priority cannot be negative.");
        }

        return new VoiceProvider
        {
            Id = Guid.CreateVersion7(),
            ProviderKey = providerKey.Trim(),
            DisplayName = displayName.Trim(),
            Priority = priority,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>The plaintext key is encrypted by the caller before reaching this method — Domain never sees it (mirrors <see cref="AIProvider.SetCredential"/>).</summary>
    public void SetCredential(string ciphertext, string? hint, string actor)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new DomainRuleViolationException("A credential is required.");
        }

        CredentialCiphertext = ciphertext;
        CredentialHint = hint;
        CredentialLastRotatedAtUtc = DateTime.UtcNow;
        Touch(actor);
    }

    public void SetDefaultVoice(string voiceId, string actor)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw new DomainRuleViolationException("A voice is required.");
        }

        if (voiceId.Length > MaxVoiceIdLength)
        {
            throw new DomainRuleViolationException($"A voice id cannot exceed {MaxVoiceIdLength} characters.");
        }

        DefaultVoiceId = voiceId.Trim();
        Touch(actor);
    }

    public void SetPriority(int priority, string actor)
    {
        if (priority < 0)
        {
            throw new DomainRuleViolationException("A voice provider's priority cannot be negative.");
        }

        if (priority == Priority)
        {
            return;
        }

        Priority = priority;
        Touch(actor);
    }

    private void Touch(string actor)
    {
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
