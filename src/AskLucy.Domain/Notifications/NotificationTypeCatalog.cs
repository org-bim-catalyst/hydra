using System.Collections.Frozen;
using System.Collections.ObjectModel;
using static AskLucy.Domain.Notifications.NotificationTypeKeys;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// The code-owned notification type catalogue (research R7, data-model.md). Read-only after
/// construction, in the style of <see cref="Authorization.AdminPermissionCatalog"/>.
/// </summary>
public static class NotificationTypeCatalog
{
    private const string AgentExecutionRoute = "/agents/{parentId}/executions/{id}";
    private const string WorkflowExecutionRoute = "/workflows/{parentId}/executions/{id}";
    private const string DocumentRoute = "/documents?documentId={id}";
    private const string KnowledgeBaseRoute = "/knowledge-bases/{id}";
    private const string MemoryRoute = "/memory?memoryId={id}";
    private const string SecuritySettingsRoute = "/settings?tab=security";

    /// <summary>The notification's own detail page; <c>{notificationId}</c> is filled at materialization.</summary>
    private const string NotificationDetailRoute = "/notifications/{notificationId}";

    private static readonly FrozenDictionary<string, NotificationTypeDefinition> _byKey;

    public static IReadOnlyList<NotificationTypeDefinition> All { get; }

    static NotificationTypeCatalog()
    {
        List<NotificationTypeDefinition> all =
        [
            // Agents
            Optional(AgentExecutionStarted, NotificationCategory.Agent, NotificationPriority.Low, ChannelDefault.Off, AgentExecutionRoute, Var("agentName", "your agent")),
            Optional(AgentExecutionCompleted, NotificationCategory.Agent, NotificationPriority.Normal, ChannelDefault.Off, AgentExecutionRoute, Var("agentName", "your agent"), Var("duration", "a moment")),
            Optional(AgentExecutionFailed, NotificationCategory.Agent, NotificationPriority.High, ChannelDefault.On, AgentExecutionRoute, Var("agentName", "your agent"), Var("failureSummary", "an unexpected error")),
            Optional(AgentApprovalRequested, NotificationCategory.Agent, NotificationPriority.High, ChannelDefault.On, AgentExecutionRoute, Var("agentName", "your agent"), Var("intendedAction", "an action")) with { IsApproval = true },

            // Workflows
            Optional(WorkflowExecutionStarted, NotificationCategory.Workflow, NotificationPriority.Low, ChannelDefault.Off, WorkflowExecutionRoute, Var("workflowName", "your workflow")),
            Optional(WorkflowExecutionCompleted, NotificationCategory.Workflow, NotificationPriority.Normal, ChannelDefault.Off, WorkflowExecutionRoute, Var("workflowName", "your workflow"), Var("duration", "a moment")),
            Optional(WorkflowExecutionFailed, NotificationCategory.Workflow, NotificationPriority.High, ChannelDefault.On, WorkflowExecutionRoute, Var("workflowName", "your workflow"), Var("failureSummary", "an unexpected error")),
            Optional(WorkflowExecutionPaused, NotificationCategory.Workflow, NotificationPriority.Normal, ChannelDefault.Off, WorkflowExecutionRoute, Var("workflowName", "your workflow")),
            Optional(WorkflowApprovalRequested, NotificationCategory.Workflow, NotificationPriority.High, ChannelDefault.On, WorkflowExecutionRoute, Var("workflowName", "your workflow"), Var("nodeName", "a step"), Var("intendedAction", "an action")) with { IsApproval = true },

            // Documents
            Optional(DocumentUploadCompleted, NotificationCategory.Document, NotificationPriority.Normal, ChannelDefault.Off, DocumentRoute, Var("documentName", "your document")),
            Optional(DocumentProcessingCompleted, NotificationCategory.Document, NotificationPriority.Normal, ChannelDefault.Off, DocumentRoute, Var("documentName", "your document")),
            Optional(DocumentProcessingFailed, NotificationCategory.Document, NotificationPriority.High, ChannelDefault.On, DocumentRoute, Var("documentName", "your document"), Var("failureSummary", "an unexpected error")),
            Optional(DocumentOcrCompleted, NotificationCategory.Document, NotificationPriority.Normal, ChannelDefault.Off, DocumentRoute, Var("documentName", "your document")),
            Optional(DocumentOcrFailed, NotificationCategory.Document, NotificationPriority.High, ChannelDefault.On, DocumentRoute, Var("documentName", "your document"), Var("failureSummary", "an unexpected error")),
            Optional(DocumentVersionCreated, NotificationCategory.Document, NotificationPriority.Normal, ChannelDefault.Off, DocumentRoute, Var("documentName", "your document"), Var("versionNumber", "a new version")),
            Optional(DocumentStorageLimitReached, NotificationCategory.Document, NotificationPriority.High, ChannelDefault.On, "/documents", Var("usedStorage", "your storage"), Var("storageLimit", "your limit")) with { RequiresItemAccess = false },
            Optional(DocumentIndexingCompleted, NotificationCategory.Document, NotificationPriority.Normal, ChannelDefault.Off, DocumentRoute, Var("documentName", "your document")),
            Optional(DocumentIndexingFailed, NotificationCategory.Document, NotificationPriority.High, ChannelDefault.On, DocumentRoute, Var("documentName", "your document"), Var("failureSummary", "an unexpected error")),

            // Knowledge bases. knowledge-base.updated waits for a non-owner actor (research R27).
            Optional(KnowledgeBaseIndexingCompleted, NotificationCategory.KnowledgeBase, NotificationPriority.Normal, ChannelDefault.Off, KnowledgeBaseRoute, Var("knowledgeBaseName", "your knowledge base")),
            Optional(KnowledgeBaseIndexingFailed, NotificationCategory.KnowledgeBase, NotificationPriority.High, ChannelDefault.On, KnowledgeBaseRoute, Var("knowledgeBaseName", "your knowledge base"), Var("failureSummary", "an unexpected error")),
            Optional(KnowledgeBaseUpdated, NotificationCategory.KnowledgeBase, NotificationPriority.Low, ChannelDefault.Off, KnowledgeBaseRoute, Var("knowledgeBaseName", "your knowledge base"), Var("changeSummary", "a change")) with { IsEmitted = false },

            // Memory
            Optional(MemoryAutoCreated, NotificationCategory.Memory, NotificationPriority.Low, ChannelDefault.Off, MemoryRoute, Var("memorySummary", "a new memory")),
            Optional(MemoryAutoApproved, NotificationCategory.Memory, NotificationPriority.Low, ChannelDefault.Off, MemoryRoute, Var("memorySummary", "a memory")),
            Optional(MemoryConflictConfirmationNeeded, NotificationCategory.Memory, NotificationPriority.Normal, ChannelDefault.Off, MemoryRoute, Var("memorySummary", "a memory")),

            // Account (email only, never in the center; FR-009c)
            AccountEmail(AccountEmailConfirmationRequested, NotificationPriority.Critical) with { SensitiveLinkKind = Notifications.SensitiveLinkKind.EmailConfirmation, RequestValidity = TimeSpan.FromHours(24) },
            AccountEmail(AccountEmailChangeRequested, NotificationPriority.Critical, Var("newEmailMasked", "your new address")) with { SensitiveLinkKind = Notifications.SensitiveLinkKind.EmailChange, RequestValidity = TimeSpan.FromHours(24) },
            AccountEmail(AccountPasswordResetRequested, NotificationPriority.Critical) with { SensitiveLinkKind = Notifications.SensitiveLinkKind.PasswordReset, RequestValidity = TimeSpan.FromMinutes(60) },
            AccountEmail(AccountSupportRequestSubmitted, NotificationPriority.High, Var("requesterEmail", "unknown"), Var("requestKind", "support"), Var("messageBody", string.Empty)),

            // Security (FR-025: minimized content)
            new NotificationTypeDefinition
            {
                Key = SecurityPasswordChanged,
                Category = NotificationCategory.Security,
                DefaultPriority = NotificationPriority.Critical,
                Channels = Channels((NotificationChannel.Email, ChannelDefault.Mandatory)),
                DeclaredVariables = [Var("changedAt", "recently"), Var("ipAddress", "an unknown address")],
                MinimizeSensitiveContent = true,
                ShowInCenter = false,
            },
            Security(SecurityTwoFactorEnabled),
            Security(SecurityTwoFactorDisabled),
            Security(SecurityRecoveryCodesRegenerated),

            // System
            new NotificationTypeDefinition
            {
                Key = SystemAnnouncementPublished,
                Category = NotificationCategory.System,
                DefaultPriority = NotificationPriority.Normal,
                // FR-004a: every announcement reaches its audience in-app; email stays optional.
                Channels = Channels((NotificationChannel.InApp, ChannelDefault.Mandatory), (NotificationChannel.Email, ChannelDefault.On)),
                DeclaredVariables =
                [
                    Var("announcementTitle", "Announcement"),
                    Var("announcementMessage", string.Empty),
                    Var("announcementKind", "Announcement"),
                    Var("endsAt", string.Empty),
                ],
                RouteTemplate = NotificationDetailRoute,
                EmailOnlyWhenCritical = true,
            },
            new NotificationTypeDefinition
            {
                Key = TemplateTest,
                Category = NotificationCategory.System,
                DefaultPriority = NotificationPriority.Normal,
                Channels = Channels((NotificationChannel.Email, ChannelDefault.Mandatory)),
                ShowInCenter = false,
                VariablesFromTemplate = true,
            },

            // Defined ahead of their emitters (FR-005)
            NotEmitted(ConversationExportCompleted, NotificationCategory.Conversation, NotificationPriority.Normal),
            NotEmitted(BillingPaymentFailed, NotificationCategory.Billing, NotificationPriority.High),
            NotEmitted(BillingSubscriptionRenewed, NotificationCategory.Billing, NotificationPriority.Normal),
        ];

        All = all.AsReadOnly();
        _byKey = all.ToFrozenDictionary(d => d.Key, StringComparer.Ordinal);
    }

    /// <summary>Throws <see cref="KeyNotFoundException"/> for an unknown key: a programming error.</summary>
    public static NotificationTypeDefinition Get(string key) =>
        TryGet(key, out var definition)
            ? definition!
            : throw new KeyNotFoundException($"Unknown notification type '{key}'.");

    public static bool TryGet(string key, out NotificationTypeDefinition? definition)
    {
        var found = _byKey.TryGetValue(key, out var value);
        definition = value;
        return found;
    }

    /// <summary>
    /// True when a user may override <paramref name="channel"/> for <paramref name="category"/>: some
    /// emitted type in the category uses the channel optionally (R28). Mandatory-only pairs are
    /// locked (FR-032), and pairs no emitted type uses have nothing to configure.
    /// </summary>
    public static bool IsConfigurable(NotificationCategory category, NotificationChannel channel) =>
        All.Any(d => d.IsEmitted
            && d.Category == category
            && d.Channels.TryGetValue(channel, out var state)
            && state != ChannelDefault.Mandatory);

    /// <summary>True when at least one emitted type in the category uses the channel at all.</summary>
    public static bool IsUsed(NotificationCategory category, NotificationChannel channel) =>
        All.Any(d => d.IsEmitted && d.Category == category && d.Supports(channel));

    /// <summary>
    /// What a user with no saved override gets for a configurable pair: on when any optional emitted type
    /// in the category has the channel on by default. A saved override replaces it for every optional type (R28).
    /// </summary>
    public static bool DefaultEnabled(NotificationCategory category, NotificationChannel channel) =>
        All.Any(d => d.IsEmitted
            && d.Category == category
            && d.Channels.TryGetValue(channel, out var state)
            && state == ChannelDefault.On);

    /// <summary>The categories that have at least one emitted type, in declaration order. Categories nothing emits yet are left out.</summary>
    public static IReadOnlyList<NotificationCategory> EmittedCategories() =>
        [.. Enum.GetValues<NotificationCategory>().Where(c => All.Any(d => d.IsEmitted && d.Category == c))];

    /// <summary>True when every emitted type in the category that uses the channel makes it mandatory (FR-032).</summary>
    public static bool IsLocked(NotificationCategory category, NotificationChannel channel)
    {
        var users = All.Where(d => d.IsEmitted && d.Category == category && d.Supports(channel)).ToList();
        return users.Count > 0 && users.All(d => d.IsMandatory(channel));
    }

    // --- construction helpers ---

    private static NotificationVariable Var(string name, string fallback) => new(name, fallback);

    private static ReadOnlyDictionary<NotificationChannel, ChannelDefault> Channels(
        params (NotificationChannel Channel, ChannelDefault State)[] channels) =>
        channels.ToDictionary(c => c.Channel, c => c.State).AsReadOnly();

    private static NotificationTypeDefinition Optional(
        string key,
        NotificationCategory category,
        NotificationPriority priority,
        ChannelDefault email,
        string route,
        params NotificationVariable[] variables) =>
        new()
        {
            Key = key,
            Category = category,
            DefaultPriority = priority,
            Channels = Channels((NotificationChannel.InApp, ChannelDefault.On), (NotificationChannel.Email, email)),
            DeclaredVariables = variables,
            RouteTemplate = route,
            RequiresItemAccess = true,
        };

    private static NotificationTypeDefinition AccountEmail(string key, NotificationPriority priority, params NotificationVariable[] variables) =>
        new()
        {
            Key = key,
            Category = NotificationCategory.Account,
            DefaultPriority = priority,
            Channels = Channels((NotificationChannel.Email, ChannelDefault.Mandatory)),
            DeclaredVariables = variables,
            ShowInCenter = false,
        };

    private static NotificationTypeDefinition Security(string key) =>
        new()
        {
            Key = key,
            Category = NotificationCategory.Security,
            DefaultPriority = NotificationPriority.Critical,
            Channels = Channels((NotificationChannel.InApp, ChannelDefault.Mandatory), (NotificationChannel.Email, ChannelDefault.Mandatory)),
            DeclaredVariables = [Var("changedAt", "recently")],
            RouteTemplate = SecuritySettingsRoute,
            MinimizeSensitiveContent = true,
        };

    private static NotificationTypeDefinition NotEmitted(string key, NotificationCategory category, NotificationPriority priority) =>
        new()
        {
            Key = key,
            Category = category,
            DefaultPriority = priority,
            Channels = Channels((NotificationChannel.InApp, ChannelDefault.On), (NotificationChannel.Email, ChannelDefault.Off)),
            IsEmitted = false,
        };
}
