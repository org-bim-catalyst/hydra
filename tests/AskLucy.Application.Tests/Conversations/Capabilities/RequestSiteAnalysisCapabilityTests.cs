using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/057-site-analysis-agent tasks.md T039 — dispatch never awaits the workflow (SC-001), explicit coordinates skip geocoding (FR-001), a second request reuses the in-flight analysis, and an unresolvable site fails in-turn (FR-023).</summary>
public sealed class RequestSiteAnalysisCapabilityTests
{
    private const string UserId = "user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ISiteAnalysisRepository _siteAnalysisRepository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly IBoundaryResolutionService _boundaryResolutionService = Substitute.For<IBoundaryResolutionService>();
    private readonly ISiteAnalysisDispatcher _dispatcher = Substitute.For<ISiteAnalysisDispatcher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RequestSiteAnalysisCapability BuildCapability() =>
        new(_userChatRepository, _siteAnalysisRepository, _geocodingProvider, _boundaryResolutionService, _dispatcher, _unitOfWork);

    private static AgentToolExecutionContext ContextFor(Guid? userChatId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), userChatId);

    [Fact]
    public async Task ExecuteAsync_ShouldReturn_WithoutAwaitingTheWorkflowItself_WhenExplicitCoordinatesAreGiven()
    {
        _siteAnalysisRepository.FindRunningForSiteAsync(UserId, UserChatId, "the specified location", Arg.Any<CancellationToken>())
            .Returns((Domain.SiteAnalysis.SiteAnalysis?)null);
        _dispatcher.DispatchAsync(Arg.Any<Guid>(), UserId, Arg.Any<string>(), Arg.Any<string>(), 25.09, 55.20, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var capability = BuildCapability();
        var input = JsonDocument.Parse("""{"latitude":25.09,"longitude":55.20}""");
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();

        // FR-001 — coordinates skip geocoding entirely.
        await _geocodingProvider.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
        await _dispatcher.Received(1).DispatchAsync(Arg.Any<Guid>(), UserId, Arg.Any<string>(), Arg.Any<string>(), 25.09, 55.20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailInTurn_WhenNoSiteCanBeIdentified()
    {
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(UserChat.Create("Chat", UserId, null, UserId));

        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var output = result.Output!.RootElement;
        output.GetProperty("started").GetBoolean().Should().BeFalse();
        output.GetProperty("reason").GetString().Should().Be("site-not-identified");
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseTheInFlightAnalysis_RatherThanDispatchingATwin()
    {
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveLocation(25.09, 55.20, "Al Barsha South", 0.9, UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var existing = Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Al Barsha South", 25.09, 55.20, null, 1, UserId);
        _siteAnalysisRepository.FindRunningForSiteAsync(UserId, UserChatId, "Al Barsha South", Arg.Any<CancellationToken>()).Returns(existing);

        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailInTurn_WithoutStartingAnAnalysis_WhenThereIsNoConversation()
    {
        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(null), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeFalse();
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, default);
    }
}
