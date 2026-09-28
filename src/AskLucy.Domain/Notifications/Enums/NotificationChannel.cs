namespace AskLucy.Domain.Notifications;

/// <summary>A delivery channel. New channels append values here and register an INotificationChannelSender (FR-060).</summary>
public enum NotificationChannel
{
    InApp,
    Email,
}
