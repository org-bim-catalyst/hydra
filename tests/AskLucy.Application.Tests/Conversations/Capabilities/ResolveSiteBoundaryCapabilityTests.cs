using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.Tests.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// The standalone-dispatch fallback found live-testing this capability (2026-09-09, see the type
/// remarks on <see cref="ResolveSiteBoundaryCapability"/>): reached outside <c>locate_a_place</c>'s
/// own hand-authored argument binding, the model supplies no latitude/longitude/name at all, so
/// the capability must resolve them itself from the chat's confirmed location instead of failing.
/// </summary>
public sealed class ResolveSiteBoundaryCapabilityTests
{
    private readonly IBoundaryResolutionService _boundaryService = Substitute.For<IBoundaryResolutionService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly Guid _chatId = Guid.NewGuid();

    private ResolveSiteBoundaryCapability BuildCapability() => new(_boundaryService, _userChatRepository, TestCorrectionMatcher.None());

    private AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), _chatId);

    private static readonly ConfirmedSiteBoundaryData Boundary = new(
        "Al Safa Park 2", 25.156, 55.2218, [], 42000, 0.7, BoundaryConfidenceLevel.Medium,
        SiteBoundarySource.OsmBoundary, "OpenStreetMap", []);

    [Fact]
    public async Task ExecuteAsync_ShouldFallBackToTheChatsActiveLocation_WhenArgumentsOmitIt()
    {
        var chat = UserChat.Create("test", "user-1", null, "user-1");
        chat.SetActiveLocation(25.156, 55.2218, "Al Safa Park 2", 0.9, "user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, Boundary, null));

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await _boundaryService.Received(1).ResolveAsync(
            Arg.Is<ConfirmedLocationData>(l => l != null && l.LocationName == "Al Safa Park 2" && l.Latitude == 25.156),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreferTheSuppliedArguments_OverTheChatsActiveLocation()
    {
        // The flow's own BindBoundaryArguments carries a same-turn result the chat record has not
        // been persisted with yet — it must win over whatever is already stored.
        var chat = UserChat.Create("test", "user-1", null, "user-1");
        chat.SetActiveLocation(1, 1, "Stale Place", 0.5, "user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, Boundary, null));

        var input = JsonDocument.Parse("""{"latitude":25.156,"longitude":55.2218,"locationName":"Al Safa Park 2","confidence":0.9}""");
        await BuildCapability().ExecuteAsync(Context(), input, CancellationToken.None);

        await _boundaryService.Received(1).ResolveAsync(
            Arg.Is<ConfirmedLocationData>(l => l != null && l.LocationName == "Al Safa Park 2"),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userChatRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The boundary lookup can fail while the site's location is perfectly well known. The outline
    /// must not be invented (specs/042 FR-012) and the failure must still be reported — but the
    /// viewer can and should still be taken to the site, which is the one visual answer left. The
    /// flow's own focus step used to exist for this and was removed in 5b51722c because it stacked a
    /// second zoom on a successful frame; nothing is stacked when no frame was drawn.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldStillMoveTheViewer_WhenTheBoundarySourceIsUnavailable()
    {
        var chat = UserChat.Create("test", "user-1", null, "user-1");
        chat.SetActiveLocation(25.156, 55.2218, "Al Safa Park 2", 0.9, "user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Unavailable, null, BoundaryConfirmationTemplates.Unavailable));

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeFalse("a lookup that produced no outline is a failure, never a confirmation");
        result.FailureReason.Should().Contain(BoundaryConfirmationTemplates.Unavailable);
        result.Output.Should().NotBeNull("the site's location is known even though its outline is not");
        result.Output!.RootElement.GetProperty("direction").GetString().Should().Be("in");
        result.Output.RootElement.TryGetProperty("polygon", out _).Should()
            .BeFalse("nothing approximate may be presented as a confirmed boundary");
    }

    /// <summary>A drawn boundary must not also emit a zoom — that is the double-zoom 5b51722c removed.</summary>
    [Fact]
    public async Task ExecuteAsync_ShouldNotIssueAZoom_WhenTheBoundaryWasDrawn()
    {
        var chat = UserChat.Create("test", "user-1", null, "user-1");
        chat.SetActiveLocation(25.156, 55.2218, "Al Safa Park 2", 0.9, "user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, Boundary, null));

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.TryGetProperty("direction", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenArgumentsAreMissingAndTheChatHasNoActiveLocation()
    {
        var chat = UserChat.Create("test", "user-1", null, "user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);

        var result = await BuildCapability().ExecuteAsync(Context(), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("confirmed location");
    }
}
