using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/077 — choosing which of the outlined site's related buildings it includes.</summary>
public sealed class SetSiteBoundaryMembersCapabilityTests
{
    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteFootprintUnion _union = Substitute.For<ISiteFootprintUnion>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly ISiteRingGeometry _geometry = Substitute.For<ISiteRingGeometry>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SetSiteBoundaryMembersCapability _capability;
    private readonly UserChat _chat = UserChat.Create("BurJuman", "user-1", null, "user-1");

    public SetSiteBoundaryMembersCapabilityTests()
    {
        _capability = new SetSiteBoundaryMembersCapability(_chats, new SiteBoundaryMembershipService(
            Substitute.For<IRelatedSiteBuildingProvider>(), _union, Substitute.For<ICapabilitySettingsReader>(),
            Substitute.For<ILogger<SiteBoundaryMembershipService>>()),
            _corrections, new EffectiveSiteBoundary(_corrections), new HandEditedMembershipComposer(_geometry), _geometry,
            _unitOfWork);

        // The union is what the real one would trace: one ring per included footprint, as mapped.
        _union.Union(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>())
            .Returns(call => call.Arg<IReadOnlyList<IReadOnlyList<GeoPoint>>>());
        _chats.GetByIdAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns(_chat);

        var boundary = Boundary(Members);
        _chat.SetActiveBoundary(
            boundary.SiteName, boundary.CentroidLatitude, boundary.CentroidLongitude, boundary.Polygon, boundary.AreaSquareMeters,
            boundary.Confidence, boundary.ConfidenceLevel, boundary.Source, boundary.SourceDetail, "user-1",
            corePolygon: Mall, members: boundary.Members);
    }

    [Fact]
    public async Task ById_IncludesExactlyTheChosenBuildings()
    {
        var result = await RunAsync("""{"memberIds":["osm_way_3"]}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        Covered(result).Should().Equal("BurJuman Mall", "Burjman Office Tower");
        result.Output!.RootElement.GetProperty("additionalPolygons").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ReportsOnlyWhatTheChoiceChanged_NotEveryBuildingLeftOut()
    {
        // On screen: the mall with its two connected buildings. Kept: the tower sharing its wall.
        var result = await RunAsync("""{"memberIds":["osm_way_1"]}""");

        Names(result, "addedBuildings").Should().BeEmpty();
        Names(result, "removedBuildings").Should().Equal("BurJuman Arjaan by Rotana");
        result.Output!.RootElement.TryGetProperty("excludedBuildings", out _).Should().BeFalse();
        result.Output.RootElement.EnumerateObject().First().Name.Should().Be("outlineCovers");
    }

    [Fact]
    public async Task AddingABuilding_ReportsItAsAdded_AndNothingRemoved()
    {
        var result = await RunAsync("""{"memberIds":["osm_way_1","osm_way_2","osm_way_3"]}""");

        Names(result, "addedBuildings").Should().Equal("Burjman Office Tower");
        Names(result, "removedBuildings").Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyList_IsTheSiteAlone()
    {
        var result = await RunAsync("""{"memberIds":[]}""");

        Covered(result).Should().Equal("BurJuman Mall");
        result.Output!.RootElement.GetProperty("areaSquareMeters").GetDouble()
            .Should().BeApproximately(GeometryMath.AreaSquareMeters(Mall), 1);
    }

    [Fact]
    public async Task ByName_MatchesTheStationAndAMistypedTower()
    {
        var result = await RunAsync("""{"memberNames":["the metro station","Burjuman Office Tower"]}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        Covered(result).Should().BeEquivalentTo("BurJuman Mall", "Burjman Office Tower", "BurJuman Metro Station");
    }

    [Fact]
    public async Task AnUnknownName_FailsAndListsTheBuildingsThatExist()
    {
        var result = await RunAsync("""{"memberNames":["Dubai Frame"]}""");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("\"Dubai Frame\"").And.Contain("BurJuman Business Tower");
    }

    /// <summary>specs/079 - "Keep the outline as it is": the same buildings, so nothing is redrawn.</summary>
    [Fact]
    public async Task Keep_WithTheBuildingsAlreadyIncluded_ReturnsKeptAndNoGeometry()
    {
        var included = string.Join(",", Members.Where(m => m.Included).Select(m => $"\"{m.Id}\""));

        var result = await RunAsync($$"""{"memberIds":[{{included}}],"keep":true}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        var root = result.Output!.RootElement;
        root.GetProperty("kept").GetBoolean().Should().BeTrue();
        root.TryGetProperty("polygon", out _).Should().BeFalse("no geometry means no redraw event");
        Covered(result).Should().StartWith("BurJuman Mall");
        _union.DidNotReceiveWithAnyArgs().Union(default!, default);
    }

    [Fact]
    public async Task Keep_WithADifferentSetOfBuildings_StillRecomposes()
    {
        var result = await RunAsync("""{"memberIds":["osm_way_3"],"keep":true}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.Output!.RootElement.TryGetProperty("kept", out _).Should().BeFalse();
        result.Output.RootElement.TryGetProperty("polygon", out _).Should().BeTrue();
    }

    [Fact]
    public void Acknowledgement_SaysTheOutlineIsBeingUpdated() =>
        _capability.AcknowledgementTemplate.Should().Be("Now updating the site outline.");

    [Fact]
    public async Task AnUnknownId_Fails()
    {
        var result = await RunAsync("""{"memberIds":["osm_way_999"]}""");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("osm_way_999");
    }

    [Fact]
    public async Task NoChoiceAtAll_Fails() =>
        (await RunAsync("{}")).Succeeded.Should().BeFalse();

    [Fact]
    public async Task WithoutRelatedBuildingsOnScreen_Fails()
    {
        _chat.SetActiveBoundary(
            "BurJuman Mall", 25.25, 55.30, Mall, 10_000, 0.9, BoundaryConfidenceLevel.High,
            SiteBoundarySource.OsmBoundary, "osm_way_100", "user-1");

        (await RunAsync("""{"memberIds":[]}""")).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task OnAHandEditedOutline_CutsTheBuildingOffAndKeepsTheUsersCorners_InOneSave()
    {
        var snapshot = new FoundSiteBoundarySnapshot(
            _chat.ActiveBoundary!.Polygon, [], Mall, _chat.ActiveBoundary.AreaSquareMeters, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "x", Members);
        var correction = SiteBoundaryCorrection.Create(
            "user-1", "BurJuman Mall", 0, 0, snapshot, [[.. Mall, Mall[0]]], 9_000, Members, "user-1");
        _chat.LinkSiteBoundaryCorrection(correction.Id, "user-1");
        _corrections.GetByIdAsync(correction.Id, "user-1", Arg.Any<CancellationToken>()).Returns(correction);
        _geometry.Intersects(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>())
            .Returns(true);
        _geometry.Cut(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(Mall);
        _geometry.UnionArea(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>()).Returns(9_999);

        // On screen: the mall with both connected buildings. Kept: the tower only, so the hotel is cut off.
        var result = await RunAsync("""{"memberIds":["osm_way_1"]}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.Output!.RootElement.EnumerateObject().First().Name.Should().Be("note");
        result.Output.RootElement.GetProperty("handEdited").GetBoolean().Should().BeTrue();
        result.Output.RootElement.GetProperty("correctionId").GetGuid().Should().Be(correction.Id);
        correction.AreaSquareMeters.Should().Be(9_999);
        correction.Members.Single(m => m.Id == "osm_way_2").Included.Should().BeFalse();
        _geometry.Received(1).Cut(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task<AgentToolResult> RunAsync(string input)
    {
        using var document = JsonDocument.Parse(input);
        return await _capability.ExecuteAsync(
            new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), _chat.Id), document);
    }

    private static IEnumerable<string?> Covered(AgentToolResult result) => Names(result, "outlineCovers");

    private static IEnumerable<string?> Names(AgentToolResult result, string property) =>
        result.Output!.RootElement.GetProperty(property).EnumerateArray().Select(e => e.GetString());
}
