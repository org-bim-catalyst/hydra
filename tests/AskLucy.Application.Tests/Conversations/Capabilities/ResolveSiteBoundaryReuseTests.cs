using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/079 FR-023 - asking for a site the user already corrected shows their outline, with no lookup.</summary>
public sealed class ResolveSiteBoundaryReuseTests
{
    private const string UserId = "user-1";

    private readonly IBoundaryResolutionService _boundaryService = Substitute.For<IBoundaryResolutionService>();
    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();

    private static readonly IReadOnlyList<GeoPoint> Found =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1550, 55.2210), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> Edited =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211), new(25.1561, 55.2211)];

    private ResolveSiteBoundaryCapability Create() =>
        new(_boundaryService, _chats, new SiteBoundaryCorrectionMatcher(_corrections));

    private static SiteBoundaryCorrection Correction(string userId = UserId) =>
        SiteBoundaryCorrection.Create(
            userId, "Muscat Grand Mall", 25.1555, 55.2215,
            new FoundSiteBoundarySnapshot(Found, [], null, 15_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [Edited], 12_345, [], userId);

    private void Stored(params SiteBoundaryCorrection[] corrections) =>
        _corrections.FindCandidatesAsync(UserId, "muscat grand mall", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SiteBoundaryCorrection>>(corrections));

    private Task<AgentToolResult> RunAsync(string input = """{"latitude":25.1555,"longitude":55.2215,"locationName":"Muscat Grand Mall","confidence":0.9}""") =>
        Create().ExecuteAsync(
            new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            JsonDocument.Parse(input), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AMatchingCorrection_IsReturnedWithoutAnyLookup()
    {
        var correction = Correction();
        Stored(correction);

        var result = await RunAsync();

        result.Succeeded.Should().BeTrue(result.FailureReason);
        await _boundaryService.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
        var root = result.Output!.RootElement;
        root.GetProperty("userCorrected").GetBoolean().Should().BeTrue();
        root.GetProperty("correctionId").GetGuid().Should().Be(correction.Id);
        root.GetProperty("areaSquareMeters").GetDouble().Should().Be(12_345);
        root.GetProperty("sourceType").GetString().Should().Be("UserCorrected");
        root.GetProperty("polygon").GetArrayLength().Should().Be(Edited.Count);
    }

    [Fact]
    public async Task TheResultLeadsWithAPlainSentenceSayingItIsTheUsersOwnOutline()
    {
        Stored(Correction());

        var result = await RunAsync();

        var first = result.Output!.RootElement.EnumerateObject().First();
        first.Name.Should().Be("outlineOrigin");
        first.Value.GetString().Should().Contain("hand-edited");
    }

    [Fact]
    public async Task ItRoundTripsThroughTheStreamPayloadWithItsCorrectionId()
    {
        var correction = Correction();
        Stored(correction);

        var result = await RunAsync();
        var chunk = StructuredPayloadExtractor.TryExtract(ResolveSiteBoundaryCapability.CapabilityKey, result.Output!.RootElement.GetRawText());

        chunk!.ConfirmedBoundary.Should().NotBeNull();
        chunk.ConfirmedBoundary!.CorrectionId.Should().Be(correction.Id);
        chunk.ConfirmedBoundary.Source.Should().Be(SiteBoundarySource.UserCorrected);
        chunk.ConfirmedBoundary.Polygon.Should().BeEquivalentTo(Edited);
    }

    [Fact]
    public async Task NoCorrection_ResolvesAsBefore_WithNoCorrectionFields()
    {
        Stored();
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Confirmed,
                new ConfirmedSiteBoundaryData("Muscat Grand Mall", 25.1555, 55.2215, Found, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                    SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
                null));

        var result = await RunAsync();

        result.Succeeded.Should().BeTrue(result.FailureReason);
        await _boundaryService.Received(1).ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        result.Output!.RootElement.GetProperty("userCorrected").GetBoolean().Should().BeFalse();
        result.Output.RootElement.GetProperty("correctionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheSameNameInAnotherPlace_ResolvesFreshInsteadOfShowingTheCorrection()
    {
        Stored(Correction());
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Confirmed,
                new ConfirmedSiteBoundaryData("Muscat Grand Mall", 40.0, -74.0, Found, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                    SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
                null));

        // Same name, but New York rather than Muscat.
        var result = await RunAsync("""{"latitude":40.0,"longitude":-74.0,"locationName":"Muscat Grand Mall","confidence":0.9}""");

        await _boundaryService.Received(1).ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        result.Output!.RootElement.GetProperty("userCorrected").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AnotherUsersCorrection_IsNeverShown()
    {
        // The repository is asked for THIS user's rows only; a row stored for someone else is simply not returned.
        _corrections.FindCandidatesAsync("someone-else", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SiteBoundaryCorrection>>([Correction("someone-else")]));
        Stored();
        _boundaryService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Confirmed,
                new ConfirmedSiteBoundaryData("Muscat Grand Mall", 25.1555, 55.2215, Found, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                    SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
                null));

        var result = await RunAsync();

        result.Output!.RootElement.GetProperty("userCorrected").GetBoolean().Should().BeFalse();
        await _corrections.DidNotReceive().FindCandidatesAsync("someone-else", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheNarratorIsToldToSayItIsTheUsersOwnOutline() =>
        Create().UsageGuidance.Should().Contain("userCorrected").And.Contain("reset");
}
