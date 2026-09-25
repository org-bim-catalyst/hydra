namespace AskLucy.Domain.Common;

/// <summary>
/// An audit row that carries the correlation id of the request or job that wrote it, so an
/// administrator can follow it to the server log line and to any operational failure of the same
/// request (specs/074 FR-006b). Stamped once, on insert, by the SaveChanges audit interceptor —
/// never by callers.
/// </summary>
public interface ICorrelated
{
    string? CorrelationId { get; }
}
