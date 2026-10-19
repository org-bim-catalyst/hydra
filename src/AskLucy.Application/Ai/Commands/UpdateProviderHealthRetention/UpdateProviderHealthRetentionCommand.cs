using MediatR;

namespace AskLucy.Application.Ai.Commands.UpdateProviderHealthRetention;

/// <summary>
/// specs/082 contracts "PUT": guarded by the row version the administrator last read. A stale version is a 409; a value
/// outside 1–365 is a 422.
/// </summary>
public sealed record UpdateProviderHealthRetentionCommand(int RetentionDays, byte[] ExpectedRowVersion)
    : IRequest<ProviderHealthRetentionDto>;
