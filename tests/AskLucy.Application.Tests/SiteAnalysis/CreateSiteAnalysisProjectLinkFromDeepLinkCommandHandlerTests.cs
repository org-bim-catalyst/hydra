using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis.Commands.CreateSiteAnalysisProjectLinkFromDeepLink;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

public sealed class CreateSiteAnalysisProjectLinkFromDeepLinkCommandHandlerTests
{
    private readonly ISiteAnalysisProjectLinkRepository _linkRepository = Substitute.For<ISiteAnalysisProjectLinkRepository>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreateSiteAnalysisProjectLinkFromDeepLinkCommandHandler CreateHandler() =>
        new(_linkRepository, _chatRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldReuseTheExistingConversation_WhenTheProjectIsAlreadyLinked()
    {
        _currentUser.UserId.Returns("user-1");
        var existingChatId = Guid.NewGuid();
        var existingLink = SiteAnalysisProjectLink.Create(existingChatId, "tdc-1", SiteAnalysisProjectLinkSource.InboundDeepLink, "Al Safa Park", null, null);
        _linkRepository.GetByTheDigitalCoreProjectIdAsync("tdc-1", Arg.Any<CancellationToken>()).Returns(existingLink);

        var result = await CreateHandler().Handle(new CreateSiteAnalysisProjectLinkFromDeepLinkCommand("tdc-1", "Al Safa Park"), CancellationToken.None);

        result.UserChatId.Should().Be(existingChatId);
        _chatRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_ShouldCreateANewConversationAndLink_WhenNoneExistsYet()
    {
        _currentUser.UserId.Returns("user-1");
        _linkRepository.GetByTheDigitalCoreProjectIdAsync("tdc-2", Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);

        var result = await CreateHandler().Handle(new CreateSiteAnalysisProjectLinkFromDeepLinkCommand("tdc-2", "Zabeel Park"), CancellationToken.None);

        result.UserChatId.Should().NotBeEmpty();
        _chatRepository.Received(1).Add(Arg.Any<Domain.Chats.UserChat>());
        _linkRepository.Received(1).Add(Arg.Is<SiteAnalysisProjectLink>(l => l.TheDigitalCoreProjectId == "tdc-2" && l.LinkSource == SiteAnalysisProjectLinkSource.InboundDeepLink));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenNoAuthenticatedUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new CreateSiteAnalysisProjectLinkFromDeepLinkCommand("tdc-3", "Some Park"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
