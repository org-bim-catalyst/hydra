namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>
/// What travels through the recorder's queue: a failure or a voice recovery. The recorder stamps
/// <see cref="CorrelationId"/> and <see cref="OccurredAtUtc"/> on the caller's thread, overwriting
/// anything the caller set, because both are lost once the work leaves the request (research D2).
/// </summary>
public abstract record OperationalFailureSignal
{
    public string? CorrelationId { get; init; }

    public DateTime OccurredAtUtc { get; init; }
}
