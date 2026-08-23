using AskLucy.Application.Abstractions;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Commands.CreateSiteAnalysisProjectLinkFromDeepLink;

public sealed class CreateSiteAnalysisProjectLinkFromDeepLinkCommandHandler(
    ISiteAnalysisProjectLinkRepository linkRepository,
    IUserChatRepository chatRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateSiteAnalysisProjectLinkFromDeepLinkCommand, CreateSiteAnalysisProjectLinkFromDeepLinkResult>
{
    public async Task<CreateSiteAnalysisProjectLinkFromDeepLinkResult> Handle(
        CreateSiteAnalysisProjectLinkFromDeepLinkCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var existingLink = await linkRepository.GetByTheDigitalCoreProjectIdAsync(request.TheDigitalCoreProjectId, cancellationToken);
        if (existingLink is not null)
        {
            // Idempotent: following the same deep link again lands back in the same conversation.
            return new CreateSiteAnalysisProjectLinkFromDeepLinkResult(existingLink.UserChatId);
        }

        var chat = UserChat.Create($"Site Analysis: {request.SiteName}", userId, sessionId: null, userId);
        chatRepository.Add(chat);

        var link = Domain.SiteAnalysis.SiteAnalysisProjectLink.Create(
            chat.Id, request.TheDigitalCoreProjectId, Domain.SiteAnalysis.SiteAnalysisProjectLinkSource.InboundDeepLink,
            request.SiteName, resolvedLatitude: null, resolvedLongitude: null);
        linkRepository.Add(link);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateSiteAnalysisProjectLinkFromDeepLinkResult(chat.Id);
    }
}
