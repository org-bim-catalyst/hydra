using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>An action the admin UI performs itself rather than navigating to a route.</summary>
public enum CorrectiveAdminAction
{
    /// <summary>The nav's action-triggered Hangfire entry (specs/060): mints a session and opens a new tab.</summary>
    OpenJobsDashboard,
}

/// <summary>The suggested fix for an incident (FR-014). Computed at read time, never stored.</summary>
public sealed record CorrectiveAction(string Text, string? AdminRoute = null, CorrectiveAdminAction? AdminAction = null);

/// <summary>
/// Code-owned mapping from what failed to how an administrator fixes it (specs/074 data-model.md →
/// Application-level types). Every engine × kind combination has non-empty text.
/// </summary>
public static class CorrectiveActionCatalog
{
    public const string ReplaceKey = "Replace the API key";
    public const string ConfigureCapability = "Enable or configure the capability";
    public const string RaiseQuota = "Raise the quota in the vendor console, or switch the default model";
    public const string CheckModel = "Check the model is still offered; switch the default model if retired";
    public const string NoActionUnlessPersists = "No action needed unless this persists";
    public const string CheckMcpServer = "Check the server's connection and credentials";
    public const string InspectJob = "Inspect the failed job in the jobs dashboard";
    public const string ReviewAccount = "Review the account if attempts continue";
    public const string OpenItem = "Open the item to see which step failed";

    /// <param name="accountEmail">
    /// Access engine only: the one account the incident involves, when there is exactly one. The
    /// route then pre-fills the users search; otherwise it opens the unfiltered list.
    /// </param>
    public static CorrectiveAction For(
        OperationalFailureEngine engine,
        OperationalFailureKind kind,
        Guid? providerId,
        OperationalFailureSubject? subject,
        string? accountEmail = null)
    {
        if (engine == OperationalFailureEngine.Access)
        {
            return kind is OperationalFailureKind.SignInRefused or OperationalFailureKind.AccountLocked or OperationalFailureKind.TwoFactorRefused
                ? new CorrectiveAction(ReviewAccount, UsersRoute(accountEmail))
                : new CorrectiveAction(NoActionUnlessPersists);
        }

        if (engine == OperationalFailureEngine.BackgroundJob)
        {
            return new CorrectiveAction(InspectJob, AdminAction: CorrectiveAdminAction.OpenJobsDashboard);
        }

        if (engine == OperationalFailureEngine.Mcp)
        {
            return new CorrectiveAction(CheckMcpServer, SelectRoute("/admin/mcp-servers", subject?.Id));
        }

        return kind switch
        {
            OperationalFailureKind.CredentialRejected or OperationalFailureKind.CredentialUnreadable =>
                new CorrectiveAction(
                    ReplaceKey,
                    SelectRoute(engine == OperationalFailureEngine.Voice ? "/admin/voice" : "/admin/ai-providers", providerId)),
            OperationalFailureKind.NotConfigured => new CorrectiveAction(ConfigureCapability, "/admin/ai-capabilities"),
            OperationalFailureKind.QuotaExhausted or OperationalFailureKind.UsageRestricted =>
                new CorrectiveAction(RaiseQuota, "/admin/default-models"),
            OperationalFailureKind.RequestInvalid or OperationalFailureKind.ResponseNotUnderstood =>
                new CorrectiveAction(CheckModel, "/admin/default-models"),
            OperationalFailureKind.RateLimited or
            OperationalFailureKind.Unavailable or
            OperationalFailureKind.TimedOut or
            OperationalFailureKind.DependencyUnreachable => new CorrectiveAction(NoActionUnlessPersists),

            // The item itself (run, document, agent execution) is linked from each occurrence, which
            // carries the ids an investigation route needs; the incident has no single item to open.
            _ => new CorrectiveAction(OpenItem),
        };
    }

    private static string SelectRoute(string path, Guid? id) => id is { } value ? $"{path}?select={value}" : path;

    private static string UsersRoute(string? email) =>
        string.IsNullOrWhiteSpace(email) ? "/admin/users" : $"/admin/users?search={Uri.EscapeDataString(email)}";
}
