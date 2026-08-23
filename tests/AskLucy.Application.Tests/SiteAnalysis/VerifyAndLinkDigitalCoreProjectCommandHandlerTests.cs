using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

public sealed class VerifyAndLinkDigitalCoreProjectCommandHandlerTests
{
    private readonly ISiteAnalysisProjectLinkRepository _linkRepository = Substitute.For<ISiteAnalysisProjectLinkRepository>();
    private readonly ITheDigitalCoreClient _client = Substitute.For<ITheDigitalCoreClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private VerifyAndLinkDigitalCoreProjectCommandHandler CreateHandler() => new(_linkRepository, _client, _unitOfWork);

    [Fact]
    public async Task Handle_ShouldLinkToExisting_WhenExactlyOneCandidateMatches()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _client.FindProjectAsync("Al Safa Park", 25.1m, 55.2m, Arg.Any<CancellationToken>())
            .Returns([new TheDigitalCoreProjectCandidate("tdc-1", "Al Safa Park", 25.1m, 55.2m, "company-1")]);

        var result = await CreateHandler().Handle(
            new VerifyAndLinkDigitalCoreProjectCommand(chatId, "Al Safa Park", 25.1m, 55.2m, UserConfirmedCreate: false), CancellationToken.None);

        result.Outcome.Should().Be(VerifyAndLinkDigitalCoreProjectOutcome.LinkedToExisting);
        result.TheDigitalCoreProjectId.Should().Be("tdc-1");
        _linkRepository.Received(1).Add(Arg.Is<SiteAnalysisProjectLink>(l => l.LinkSource == SiteAnalysisProjectLinkSource.BootstrapMatched));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateProjectAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnAmbiguousCandidates_WhenMultipleMatch_AndNotCreateOrLink()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _client.FindProjectAsync("Al Safa Park", null, null, Arg.Any<CancellationToken>())
            .Returns([
                new TheDigitalCoreProjectCandidate("tdc-1", "Al Safa Park", null, null, "company-1"),
                new TheDigitalCoreProjectCandidate("tdc-2", "Al Safa Park", null, null, "company-2"),
            ]);

        var result = await CreateHandler().Handle(
            new VerifyAndLinkDigitalCoreProjectCommand(chatId, "Al Safa Park", null, null, UserConfirmedCreate: false), CancellationToken.None);

        result.Outcome.Should().Be(VerifyAndLinkDigitalCoreProjectOutcome.AmbiguousCandidates);
        result.AmbiguousCandidates.Should().HaveCount(2);
        _linkRepository.DidNotReceive().Add(Arg.Any<SiteAnalysisProjectLink>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnAwaitingCreateConfirmation_WhenNoCandidateMatches_AndNotCreate()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _client.FindProjectAsync("Al Safa Park", null, null, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(
            new VerifyAndLinkDigitalCoreProjectCommand(chatId, "Al Safa Park", null, null, UserConfirmedCreate: false), CancellationToken.None);

        result.Outcome.Should().Be(VerifyAndLinkDigitalCoreProjectOutcome.AwaitingCreateConfirmation);
        await _client.DidNotReceive().CreateProjectAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCreateProject_OnlyWhenUserConfirmedCreateIsTrue()
    {
        var chatId = Guid.NewGuid();
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((SiteAnalysisProjectLink?)null);
        _client.CreateProjectAsync("Al Safa Park", 25.1m, 55.2m, Arg.Any<CancellationToken>()).Returns("tdc-new-1");

        var result = await CreateHandler().Handle(
            new VerifyAndLinkDigitalCoreProjectCommand(chatId, "Al Safa Park", 25.1m, 55.2m, UserConfirmedCreate: true), CancellationToken.None);

        result.Outcome.Should().Be(VerifyAndLinkDigitalCoreProjectOutcome.Created);
        result.TheDigitalCoreProjectId.Should().Be("tdc-new-1");
        _linkRepository.Received(1).Add(Arg.Is<SiteAnalysisProjectLink>(l => l.LinkSource == SiteAnalysisProjectLinkSource.BootstrapCreated));
        await _client.DidNotReceive().FindProjectAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnLinkedToExisting_WithoutCallingTheDigitalCore_WhenAlreadyLinked()
    {
        var chatId = Guid.NewGuid();
        var existingLink = SiteAnalysisProjectLink.Create(chatId, "tdc-existing", SiteAnalysisProjectLinkSource.InboundDeepLink, "Al Safa Park", null, null);
        _linkRepository.GetByUserChatIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(existingLink);

        var result = await CreateHandler().Handle(
            new VerifyAndLinkDigitalCoreProjectCommand(chatId, "Al Safa Park", null, null, UserConfirmedCreate: false), CancellationToken.None);

        result.Outcome.Should().Be(VerifyAndLinkDigitalCoreProjectOutcome.LinkedToExisting);
        result.TheDigitalCoreProjectId.Should().Be("tdc-existing");
        await _client.DidNotReceive().FindProjectAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }
}
