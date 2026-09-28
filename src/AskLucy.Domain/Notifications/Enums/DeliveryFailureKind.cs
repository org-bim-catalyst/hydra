namespace AskLucy.Domain.Notifications;

/// <summary>Why a delivery failed. AmbiguousOutcome means a send crashed mid-flight and is never resent automatically (research R5).</summary>
public enum DeliveryFailureKind
{
    Transient,
    Permanent,
    AmbiguousOutcome,
    RetryLimitReached,
    RecipientUnavailable,
    RequestExpired,
    RenderError,
}
