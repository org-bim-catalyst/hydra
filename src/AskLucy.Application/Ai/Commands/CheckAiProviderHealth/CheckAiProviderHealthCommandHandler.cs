using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.CheckAiProviderHealth;

/// <summary>
/// specs/043 FR-024. Deliberately mirrors <c>ProviderHealthCheckHostedService</c>'s per-provider
/// body rather than inventing a second way to record a health outcome: same probe, same
/// classification, same append-only history row, differing only in actor and in being
/// administrator-initiated.
///
/// Concurrency (FR-025) is bounded by the controller's existing <c>admin-endpoints</c>
/// rate-limit policy, not by new machinery here (research.md Decision 8).
/// </summary>
public sealed class CheckAiProviderHealthCommandHandler(
    IAIProviderRepository providers,
    IProviderHealthCheckRepository healthChecks,
    IAIProviderResolver resolver,
    IProviderHealthFreshnessPolicy freshnessPolicy,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<CheckAiProviderHealthCommandHandler> logger)
    : IRequestHandler<CheckAiProviderHealthCommand, CheckAiProviderHealthResultDto>
{
    public async Task<CheckAiProviderHealthResultDto> Handle(CheckAiProviderHealthCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        var checkedAtUtc = DateTime.UtcNow;

        // A provider that is failing is not an error here — the *check* succeeded, and its
        // finding belongs in the payload. Only a failure of the checking mechanism itself
        // propagates, which the API boundary turns into a 5xx rather than a false "unhealthy"
        // recorded against the provider (FR-023).
        var aiProvider = resolver.Resolve(provider.ProviderKey);
        var result = await aiProvider.CheckHealthAsync(cancellationToken);

        provider.UpdateHealthStatus(result.IsHealthy, checkedAtUtc, result.Kind, result.Reason);
        healthChecks.Add(ProviderHealthCheck.Create(
            provider.Id,
            checkedAtUtc,
            result.IsHealthy,
            result.Reason,
            actor: $"admin:{actorUserId}",
            result.Kind,
            result.Reason));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminAiProviderActionPerformed(
            logger,
            "check-health",
            actorUserId,
            provider.Id,
            result.IsHealthy ? "healthy" : $"unhealthy ({result.Kind})");

        return new CheckAiProviderHealthResultDto(
            provider.HealthStatus,
            provider.HealthFailureKind,
            provider.HealthFailureReason,
            checkedAtUtc,
            freshnessPolicy.StaleAfterUtc(checkedAtUtc));
    }
}
