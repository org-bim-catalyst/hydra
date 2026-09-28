namespace AskLucy.Domain.Notifications;

/// <summary>Who a delivery goes to. SupportMailbox addresses come from server configuration only, at send time (FR-009c).</summary>
public enum RecipientKind
{
    User,
    Address,
    SupportMailbox,
}
