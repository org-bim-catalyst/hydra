using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/079 - opening the outline editor: owner only, and only with an outline.</summary>
public sealed class EditSiteBoundaryCapabilityTests
{
    private static readonly IReadOnlyList<GeoPoint> Ring =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1560, 55.2210)];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly IRoleAuditLogRepository _audit = Substitute.For<IRoleAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private EditSiteBoundaryCapability Create() => new(
        _chats, new EffectiveSiteBoundary(_corrections), new ChatOwnershipAuditor(_audit, _unitOfWork));

    private UserChat ChatWithOutline(string owner = "owner-1")
    {
        var chat = UserChat.Create("Chat", owner, null, owner);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, Ring, 48_860, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        return chat;
    }

    private static AgentToolExecutionContext ContextFor(Guid? chatId, string userId = "owner-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), Guid.NewGuid(), chatId);

    private Task<AgentToolResult> RunAsync(Guid? chatId, string userId = "owner-1")
    {
        using var input = JsonDocument.Parse("{}");
        return Create().ExecuteAsync(ContextFor(chatId, userId), input, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Owner_GetsTheEditorOpenedWithTheRevisionInForce()
    {
        var chat = ChatWithOutline();

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        var root = result.Output!.RootElement;
        root.GetProperty("openEditor").GetBoolean().Should().BeTrue();
        root.GetProperty("chatId").GetString().Should().Be(chat.Id.ToString());
        root.GetProperty("revision").GetString().Should().Be(chat.ActiveBoundary!.Revision.ToString());
        root.GetProperty("siteName").GetString().Should().Be("Muscat Grand Mall");
        root.GetProperty("isHandEdited").GetBoolean().Should().BeFalse();
        root.GetProperty("howToEdit").GetString().Should().Contain("Drag a corner");
    }

    [Fact]
    public async Task Owner_OfAHandEditedOutline_GetsTheCorrectionsRevision()
    {
        var chat = ChatWithOutline();
        var correction = SiteBoundaryCorrection.Create(
            "owner-1", "Muscat Grand Mall", 25.1555, 55.2215,
            new FoundSiteBoundarySnapshot(Ring, [], null, 48_860, 0.7, BoundaryConfidenceLevel.Medium,
                SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [Ring], 40_000, [], "owner-1");
        chat.LinkSiteBoundaryCorrection(correction.Id, "owner-1");
        _corrections.GetByIdAsync(correction.Id, "owner-1", Arg.Any<CancellationToken>()).Returns(correction);

        var result = await RunAsync(chat.Id);

        result.Output!.RootElement.GetProperty("isHandEdited").GetBoolean().Should().BeTrue();
        result.Output.RootElement.GetProperty("revision").GetString().Should().Be(correction.Revision.ToString());
        result.Output.RootElement.GetProperty("voidCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task NonOwner_IsRefusedAndTheDenialAudited()
    {
        var chat = ChatWithOutline("owner-1");

        var result = await RunAsync(chat.Id, userId: "intruder");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Only the chat's owner");
        _audit.Received(1).Add(Arg.Is<RoleAuditLog>(e => e!.Action == RoleAuditAction.AuthorizationDenied && e.ActorUserId == "intruder"));
    }

    [Fact]
    public async Task ChatWithoutAnOutline_IsRefused()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("no outlined site");
    }

    [Fact]
    public async Task NoChat_IsRefused() =>
        (await RunAsync(chatId: null)).Succeeded.Should().BeFalse();

    [Fact]
    public void IsNeverOfferedThroughTheGenericOffer_AndOnlyAvailableWithAnOutline()
    {
        var capability = Create();
        var withOutline = new TurnContext(
            "owner-1", Guid.NewGuid(), null,
            new ActiveSiteBoundary("S", 1, 1, Ring, 1, 0.5, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "x"),
            [], false, true, 0, new HashSet<AgentToolPermission>(), null);

        capability.IsAvailable(withOutline).Should().BeTrue();
        capability.IsAvailable(withOutline with { ActiveBoundary = null }).Should().BeFalse();
        capability.IsOfferable(withOutline, TurnOutcome.None).Should().BeFalse();
    }
}
