using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CheckAiProviderHealth;

/// <summary>
/// specs/043 FR-024 — probes one provider now and records the classified outcome, so an
/// administrator who has just replaced a credential or enabled billing can confirm the fix
/// without waiting for the next background cycle.
///
/// A command rather than a query: it appends a <see cref="ProviderHealthCheck"/> row and
/// updates the provider's current state (constitution §3, CQRS rules).
/// </summary>
public sealed record CheckAiProviderHealthCommand(Guid ProviderId) : IRequest<CheckAiProviderHealthResultDto>;

/// <summary>contracts/admin-provider-health-api.md §2.</summary>
public sealed record CheckAiProviderHealthResultDto(
    ProviderHealthStatus HealthStatus,
    AiProviderFailureKind? HealthFailureKind,
    string? HealthFailureReason,
    DateTime CheckedAtUtc,
    DateTime? HealthStaleAfterUtc);
