namespace AskLucy.Domain.Notifications;

/// <summary>Only Immediate is offered in this feature; digests are stored only (FR-033).</summary>
public enum DeliveryFrequency
{
    Immediate,
    DailyDigest,
    WeeklyDigest,
}
