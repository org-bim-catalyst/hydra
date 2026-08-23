using AskLucy.Application.Abstractions;
using AskLucy.Domain.SiteAnalysis;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;

public sealed class VerifyAndLinkDigitalCoreProjectCommandHandler(
    ISiteAnalysisProjectLinkRepository linkRepository,
    ITheDigitalCoreClient theDigitalCoreClient,
    IUnitOfWork unitOfWork) : IRequestHandler<VerifyAndLinkDigitalCoreProjectCommand, VerifyAndLinkDigitalCoreProjectResult>
{
    public async Task<VerifyAndLinkDigitalCoreProjectResult> Handle(VerifyAndLinkDigitalCoreProjectCommand request, CancellationToken cancellationToken)
    {
        var existingLink = await linkRepository.GetByUserChatIdAsync(request.UserChatId, cancellationToken);
        if (existingLink is not null)
        {
            return new VerifyAndLinkDigitalCoreProjectResult(
                VerifyAndLinkDigitalCoreProjectOutcome.LinkedToExisting, existingLink.TheDigitalCoreProjectId, null);
        }

        if (request.UserConfirmedCreate)
        {
            // FR-001f: only ever reached after explicit user confirmation.
            var createdProjectId = await theDigitalCoreClient.CreateProjectAsync(
                request.SiteName, request.Latitude, request.Longitude, cancellationToken);

            var createdLink = SiteAnalysisProjectLink.Create(
                request.UserChatId, createdProjectId, SiteAnalysisProjectLinkSource.BootstrapCreated,
                request.SiteName, request.Latitude, request.Longitude);
            linkRepository.Add(createdLink);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new VerifyAndLinkDigitalCoreProjectResult(VerifyAndLinkDigitalCoreProjectOutcome.Created, createdProjectId, null);
        }

        // FR-001c: name-first, geolocation-secondary search (research.md Decision 8).
        var candidates = await theDigitalCoreClient.FindProjectAsync(request.SiteName, request.Latitude, request.Longitude, cancellationToken);

        if (candidates.Count == 1)
        {
            // FR-001d: a single confident match links rather than offering to create a duplicate.
            var matchedLink = SiteAnalysisProjectLink.Create(
                request.UserChatId, candidates[0].ProjectId, SiteAnalysisProjectLinkSource.BootstrapMatched,
                request.SiteName, request.Latitude, request.Longitude);
            linkRepository.Add(matchedLink);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new VerifyAndLinkDigitalCoreProjectResult(VerifyAndLinkDigitalCoreProjectOutcome.LinkedToExisting, candidates[0].ProjectId, null);
        }

        if (candidates.Count > 1)
        {
            // Edge case (spec.md): multiple plausible candidates \u2014 ask the user, never pick silently.
            return new VerifyAndLinkDigitalCoreProjectResult(
                VerifyAndLinkDigitalCoreProjectOutcome.AmbiguousCandidates, null,
                candidates.Select(c => new TheDigitalCoreProjectCandidateDto(c.ProjectId, c.Name)).ToList());
        }

        // FR-001e: no match \u2014 offer to create, wait for confirmation.
        return new VerifyAndLinkDigitalCoreProjectResult(VerifyAndLinkDigitalCoreProjectOutcome.AwaitingCreateConfirmation, null, null);
    }
}
