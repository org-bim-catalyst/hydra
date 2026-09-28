namespace AskLucy.Domain.Notifications;

/// <summary>The area a notification type belongs to; preferences are set per category and channel (FR-031).</summary>
public enum NotificationCategory
{
    Security,
    Account,
    Agent,
    Workflow,
    Document,
    KnowledgeBase,
    Memory,
    System,
    Conversation,
    Billing,
}
