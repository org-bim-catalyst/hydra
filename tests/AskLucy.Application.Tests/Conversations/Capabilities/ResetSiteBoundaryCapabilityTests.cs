using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Chats.Commands.ResetSiteBoundary;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/079 (US5) - the chat-side reset: it sends the same command as the REST endpoint and returns the found outline.</summary>
public sealed class ResetSiteBoundaryCapabilityTests
{
    private const string Owner = "owner-1";

    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1550, 55.2210), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211), new(25.1561, 55.2211)];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();

    private ResetSiteBoundaryCapability Create() => new(_chats, new EffectiveSiteBoundary(_corrections), _sender);

    private UserChat ChatWithOutline(bool handEdited)
    {
        var chat = UserChat.Create("Chat", Owner, null, Owner);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, FoundRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", Owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        if (handEdited)
        {
            var found = chat.ActiveBoundary!;
            var snapshot = new FoundSiteBoundarySnapshot(
                found.Polygon, found.AdditionalPolygons, found.CorePolygon, found.AreaSquareMeters, found.Confidence,
                found.ConfidenceLevel, found.Source, found.SourceDetail, found.Members);
            var correction = SiteBoundaryCorrection.Create(
                Owner, found.SiteName, found.CentroidLatitude, found.CentroidLongitude, snapshot, [EditedRing], 14_321.5, found.Members, Owner);
            chat.LinkSiteBoundaryCorrection(correction.Id, Owner);
            _corrections.GetByIdAsync(correction.Id, Owner, Arg.Any<CancellationToken>()).Returns(correction);
        }

        return chat;
    }

    private async Task<AgentToolResult> RunAsync(Guid? chatId)
    {
        using var input = JsonDocument.Parse("{}");
        return await Create().ExecuteAsync(
            new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Owner, Guid.NewGuid(), Guid.NewGuid(), chatId), input, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandEdited_SendsTheResetCommandWithTheRevisionInForce_AndReturnsTheFoundOutline()
    {
        var chat = ChatWithOutline(handEdited: true);
        var revisionInForce = (await new EffectiveSiteBoundary(_corrections).ResolveAsync(chat, TestContext.Current.CancellationToken))!.Revision;
        // The command handler would unlink the chat; the substitute does it here so the capability reads the found outline.
        _sender.Send(Arg.Any<ResetSiteBoundaryCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                chat.UnlinkSiteBoundaryCorrection(Owner);
                return Task.FromResult<ResetSiteBoundaryResult>(null!);
            });

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        await _sender.Received(1).Send(
            Arg.Is<ResetSiteBoundaryCommand>(c => c!.ChatId == chat.Id && c.ExpectedRevision == revisionInForce.ToString()), Arg.Any<CancellationToken>());
        var root = result.Output!.RootElement;
        root.EnumerateObject().First().Name.Should().Be("note");
        root.GetProperty("resetFromHandEdit").GetBoolean().Should().BeTrue();
        root.GetProperty("areaSquareMeters").GetDouble().Should().Be(15_000);
        root.GetProperty("polygon").GetArrayLength().Should().BeGreaterThan(2);
        root.GetProperty("userCorrected").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task NeverEdited_SaysThereWasNothingToPutBack_AndSendsNothing()
    {
        var chat = ChatWithOutline(handEdited: false);

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reset").GetBoolean().Should().BeFalse();
        await _sender.DidNotReceive().Send(Arg.Any<ResetSiteBoundaryCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotTheOwner_ReportsItInWords()
    {
        var chat = ChatWithOutline(handEdited: true);
        _sender.Send(Arg.Any<ResetSiteBoundaryCommand>(), Arg.Any<CancellationToken>()).Returns<Task<ResetSiteBoundaryResult>>(_ => throw new KeyNotFoundException());

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("owner");
    }

    [Fact]
    public async Task AStaleRevision_ReportsItInWords()
    {
        var chat = ChatWithOutline(handEdited: true);
        _sender.Send(Arg.Any<ResetSiteBoundaryCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<ResetSiteBoundaryResult>>(_ => throw new ConcurrencyConflictException("changed", Guid.NewGuid().ToString()));

        var result = await RunAsync(chat.Id);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("changed");
    }

    [Fact]
    public async Task NoChat_OrNoOutline_IsAFailureInWords()
    {
        (await RunAsync(null)).Succeeded.Should().BeFalse();

        var empty = UserChat.Create("Chat", Owner, null, Owner);
        _chats.GetByIdAsync(empty.Id, Arg.Any<CancellationToken>()).Returns(empty);
        (await RunAsync(empty.Id)).Succeeded.Should().BeFalse();
    }
}
