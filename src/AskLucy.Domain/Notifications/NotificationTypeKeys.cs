namespace AskLucy.Domain.Notifications;

/// <summary>
/// Catalogue keys for every notification type (specs/067 data-model.md). Emitters use these
/// constants, never string literals, so a renamed or removed type fails the build.
/// </summary>
public static class NotificationTypeKeys
{
    public const string AgentExecutionStarted = "agent.execution.started";
    public const string AgentExecutionCompleted = "agent.execution.completed";
    public const string AgentExecutionFailed = "agent.execution.failed";
    public const string AgentApprovalRequested = "agent.approval.requested";

    public const string WorkflowExecutionStarted = "workflow.execution.started";
    public const string WorkflowExecutionCompleted = "workflow.execution.completed";
    public const string WorkflowExecutionFailed = "workflow.execution.failed";
    public const string WorkflowExecutionPaused = "workflow.execution.paused";
    public const string WorkflowApprovalRequested = "workflow.approval.requested";

    public const string DocumentUploadCompleted = "document.upload.completed";
    public const string DocumentProcessingCompleted = "document.processing.completed";
    public const string DocumentProcessingFailed = "document.processing.failed";
    public const string DocumentOcrCompleted = "document.ocr.completed";
    public const string DocumentOcrFailed = "document.ocr.failed";
    public const string DocumentVersionCreated = "document.version.created";
    public const string DocumentStorageLimitReached = "document.storage.limit-reached";
    public const string DocumentIndexingCompleted = "document.indexing.completed";
    public const string DocumentIndexingFailed = "document.indexing.failed";

    public const string KnowledgeBaseIndexingCompleted = "knowledge-base.indexing.completed";
    public const string KnowledgeBaseIndexingFailed = "knowledge-base.indexing.failed";
    public const string KnowledgeBaseUpdated = "knowledge-base.updated";

    public const string MemoryAutoCreated = "memory.auto-created";
    public const string MemoryAutoApproved = "memory.auto-approved";
    public const string MemoryConflictConfirmationNeeded = "memory.conflict.confirmation-needed";

    public const string AccountEmailConfirmationRequested = "account.email-confirmation.requested";
    public const string AccountEmailChangeRequested = "account.email-change.requested";
    public const string AccountPasswordResetRequested = "account.password-reset.requested";
    public const string AccountSupportRequestSubmitted = "account.support-request.submitted";

    public const string SecurityPasswordChanged = "security.password-changed";
    public const string SecurityTwoFactorEnabled = "security.two-factor.enabled";
    public const string SecurityTwoFactorDisabled = "security.two-factor.disabled";
    public const string SecurityRecoveryCodesRegenerated = "security.recovery-codes.regenerated";

    public const string SystemAnnouncementPublished = "system.announcement.published";
    public const string TemplateTest = "template.test";

    public const string ConversationExportCompleted = "conversation.export.completed";
    public const string BillingPaymentFailed = "billing.payment.failed";
    public const string BillingSubscriptionRenewed = "billing.subscription.renewed";
}
