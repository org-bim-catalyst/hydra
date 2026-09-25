using AskLucy.Domain.Ai;

namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// The classified cause of a failure (specs/074 research D4). The first nine members mirror
/// <see cref="AiProviderFailureKind"/> by name; the rest cover non-provider failures.
/// </summary>
public enum OperationalFailureKind
{
    CredentialRejected,
    CredentialUnreadable,
    NotConfigured,
    QuotaExhausted,
    RateLimited,
    UsageRestricted,
    Unavailable,
    RequestInvalid,
    ResponseNotUnderstood,
    UnexpectedError,
    TimedOut,
    DependencyUnreachable,
    ValidationFailed,
    JobFailedAfterRetries,
    SignInRefused,
    TwoFactorRefused,
    AccountLocked,
    AccessDenied,
}

/// <summary>Conversions between the provider vocabulary and <see cref="OperationalFailureKind"/>.</summary>
public static class OperationalFailureKinds
{
    /// <summary>
    /// Total mapping: a new <see cref="AiProviderFailureKind"/> member fails here (and in the
    /// same-name test) rather than silently becoming <see cref="OperationalFailureKind.UnexpectedError"/>.
    /// </summary>
    public static OperationalFailureKind FromProvider(AiProviderFailureKind kind) => kind switch
    {
        AiProviderFailureKind.CredentialRejected => OperationalFailureKind.CredentialRejected,
        AiProviderFailureKind.CredentialUnreadable => OperationalFailureKind.CredentialUnreadable,
        AiProviderFailureKind.NotConfigured => OperationalFailureKind.NotConfigured,
        AiProviderFailureKind.QuotaExhausted => OperationalFailureKind.QuotaExhausted,
        AiProviderFailureKind.RateLimited => OperationalFailureKind.RateLimited,
        AiProviderFailureKind.UsageRestricted => OperationalFailureKind.UsageRestricted,
        AiProviderFailureKind.Unavailable => OperationalFailureKind.Unavailable,
        AiProviderFailureKind.RequestInvalid => OperationalFailureKind.RequestInvalid,
        AiProviderFailureKind.ResponseNotUnderstood => OperationalFailureKind.ResponseNotUnderstood,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped provider failure kind."),
    };

    /// <summary>True for the kinds a provider classifies — their root cause ignores engine, operation and subject (FR-026a).</summary>
    public static bool IsProviderKind(OperationalFailureKind kind) => kind switch
    {
        OperationalFailureKind.CredentialRejected or
        OperationalFailureKind.CredentialUnreadable or
        OperationalFailureKind.NotConfigured or
        OperationalFailureKind.QuotaExhausted or
        OperationalFailureKind.RateLimited or
        OperationalFailureKind.UsageRestricted or
        OperationalFailureKind.Unavailable or
        OperationalFailureKind.RequestInvalid or
        OperationalFailureKind.ResponseNotUnderstood => true,
        _ => false,
    };
}
