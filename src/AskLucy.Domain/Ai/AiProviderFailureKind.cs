namespace AskLucy.Domain.Ai;

/// <summary>
/// How a single interaction with an AI provider failed (specs/043 FR-001). Every provider
/// failure classifies to exactly one member, derived from the vendor's own machine-readable
/// reason first and the HTTP status second (FR-002).
///
/// Lives in Domain, not Application, because <see cref="AIProvider"/> and
/// <see cref="ProviderHealthCheck"/> persist it as part of their own state — Domain may
/// reference nothing, so the vocabulary a Domain entity stores has to be owned here
/// (constitution §3). Application's <c>AiProviderException</c> hierarchy and the Problem
/// Details boundary both read it from here.
///
/// There is deliberately no <c>InternalError</c> member: FR-007 reserves that condition for
/// failures originating inside Ask Lucy, which are represented by the *absence* of a
/// classified provider exception and continue to map to the generic 500 fallback.
/// </summary>
public enum AiProviderFailureKind
{
    /// <summary>The vendor refused the configured credential — an administrator must replace the API key.</summary>
    CredentialRejected,

    /// <summary>The stored credential ciphertext could not be decrypted, e.g. the Data Protection key ring changed — an administrator must re-enter the key.</summary>
    CredentialUnreadable,

    /// <summary>No credential is configured for this provider.</summary>
    NotConfigured,

    /// <summary>The project or account usage allowance is spent. Distinct from <see cref="RateLimited"/>: waiting minutes will not help.</summary>
    QuotaExhausted,

    /// <summary>A short-term throughput limit. Retrying shortly is the correct response.</summary>
    RateLimited,

    /// <summary>Billing is disabled, or the API is not enabled for the project — actionable at the vendor's console, not by changing the key.</summary>
    UsageRestricted,

    /// <summary>A vendor outage, network failure, or timeout.</summary>
    Unavailable,

    /// <summary>The vendor rejected this specific request as malformed or unusable.</summary>
    RequestInvalid,

    /// <summary>The response could not be parsed or did not carry the expected shape.</summary>
    ResponseNotUnderstood,
}
