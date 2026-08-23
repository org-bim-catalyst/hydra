using MediatR;

namespace AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;

public enum VerifyAndLinkDigitalCoreProjectOutcome
{
    LinkedToExisting,
    AwaitingCreateConfirmation,
    Created,
    AmbiguousCandidates,
}

public sealed record VerifyAndLinkDigitalCoreProjectResult(
    VerifyAndLinkDigitalCoreProjectOutcome Outcome,
    string? TheDigitalCoreProjectId,
    IReadOnlyList<TheDigitalCoreProjectCandidateDto>? AmbiguousCandidates);

public sealed record TheDigitalCoreProjectCandidateDto(string ProjectId, string Name);

/// <summary>
/// FR-001c-FR-001g: searches TheDigitalCore for a matching Project (name-then-geolocation,
/// research.md Decision 8); if <see cref="UserConfirmedCreate"/> is true, creates one instead
/// (only ever called with this flag set after the user has explicitly confirmed \u2014 FR-001e/FR-001f).
/// Deliberately has no HTTP-bound dependency (no <c>ICurrentUserAccessor</c>) so it can be invoked
/// both from an authenticated request (a confirmation reply) and from a Hangfire background job
/// (the initial search after <c>resolve_site_boundary</c> completes) with no behavior difference.
/// </summary>
public sealed record VerifyAndLinkDigitalCoreProjectCommand(
    Guid UserChatId,
    string SiteName,
    decimal? Latitude,
    decimal? Longitude,
    bool UserConfirmedCreate) : IRequest<VerifyAndLinkDigitalCoreProjectResult>;
