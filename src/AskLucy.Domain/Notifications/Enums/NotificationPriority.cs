namespace AskLucy.Domain.Notifications;

/// <summary>Drives default channels, retry schedule and visual prominence (spec Assumptions: priority defaults).</summary>
public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Critical,
}
