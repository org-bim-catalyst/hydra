using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// The only two sentences a non-administrator ever sees for a system-side failure (specs/074
/// research D9, FR-002/FR-005). The classified cause belongs on the admin trail, never here.
/// </summary>
public static class UserFacingFailureText
{
    /// <summary>For failures a retry may fix.</summary>
    public const string Retry = "Something went wrong and I couldn't finish. Please try again.";

    /// <summary>For failures only an administrator can fix, where retrying cannot help.</summary>
    public const string Later = "This isn't available right now. Please try again later.";

    public static string For(OperationalFailureKind? kind) => kind switch
    {
        OperationalFailureKind.NotConfigured or
        OperationalFailureKind.CredentialRejected or
        OperationalFailureKind.CredentialUnreadable or
        OperationalFailureKind.QuotaExhausted or
        OperationalFailureKind.UsageRestricted => Later,
        _ => Retry,
    };
}
