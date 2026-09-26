using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.OperationalFailures;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.GetIncident;

public sealed class GetIncidentQueryHandler(
    IOperationalFailureStore store,
    OperationalFailureReadModelBuilder readModels,
    IAIProviderRepository aiProviders,
    IEffectivePermissionResolver permissionResolver,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetIncidentQuery, IncidentDetailDto>
{
    private const int SampleUserCount = 10;

    public async Task<IncidentDetailDto> Handle(GetIncidentQuery request, CancellationToken cancellationToken)
    {
        var incident = await store.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Incident {request.IncidentId} was not found.");

        var summary = (await readModels.SummariesAsync([incident], cancellationToken))[0];

        var sampleUserIds = await store.ListRecentUserIdsAsync(incident.Id, SampleUserCount, cancellationToken);
        var users = await readModels.UsersAsync(
            [.. sampleUserIds, incident.AcknowledgedByUserId, incident.ResolvedByUserId], cancellationToken);
        var sampleUsers = sampleUserIds.Select(id => OperationalFailureReadModelBuilder.UserRef(id, users)).ToList();

        // The Access engine's fix is on the account itself, so its route pre-fills the one account involved.
        var accountEmail = incident.Engine == OperationalFailureEngine.Access && incident.DistinctUserCount == 1
            ? sampleUsers.SingleOrDefault()?.Email
            : null;
        var subject = summary.Subject is { } s ? new OperationalFailureSubject(s.Type, s.Id, s.Label) : null;
        var action = CorrectiveActionCatalog.For(incident.Engine, incident.Kind, incident.ProviderId, subject, accountEmail);

        var permissions = currentUser.UserId is { } viewerId
            ? await permissionResolver.ResolveAsync(viewerId, cancellationToken)
            : PermissionSet.Empty;

        return new IncidentDetailDto(
            summary,
            new CorrectiveActionDto(action.Text, action.AdminRoute, action.AdminAction),
            sampleUsers,
            canManage: permissions.Contains(AdminPermissionCatalog.OperationalFailuresManage),
            canViewContent: permissions.Contains(AdminPermissionCatalog.OperationalFailuresContentView))
        {
            RecurrenceOfIncidentId = incident.RecurrenceOfIncidentId,
            Acknowledged = incident is { AcknowledgedByUserId: { } acknowledgedBy, AcknowledgedAtUtc: { } acknowledgedAt }
                ? new IncidentAcknowledgementDto(OperationalFailureReadModelBuilder.UserRef(acknowledgedBy, users), acknowledgedAt)
                : null,
            Resolved = incident is { ResolvedByUserId: { } resolvedBy, ResolvedAtUtc: { } resolvedAt }
                ? new IncidentResolutionDto(OperationalFailureReadModelBuilder.UserRef(resolvedBy, users), resolvedAt, incident.ResolutionNote)
                : null,
            ProviderHealth = await ProviderHealthAsync(incident.ProviderId, cancellationToken),
        };
    }

    /// <summary>FR-017: the provider's own latest health check, shown beside the incident. Null for a non-AI provider (a voice engine, say).</summary>
    private async Task<ProviderHealthDto?> ProviderHealthAsync(Guid? providerId, CancellationToken cancellationToken)
    {
        if (providerId is not { } id || await aiProviders.GetByIdAsync(id, cancellationToken) is not { } provider)
        {
            return null;
        }

        return new ProviderHealthDto(provider.HealthStatus, provider.HealthFailureKind, provider.HealthStatusCheckedAtUtc);
    }
}
