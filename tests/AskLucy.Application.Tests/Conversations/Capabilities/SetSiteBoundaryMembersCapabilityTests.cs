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
    private readonly SetSiteBoundaryMembersCapability _capability;
    private readonly UserChat _chat = UserChat.Create("BurJuman", "user-1", null, "user-1");

    public SetSiteBoundaryMembersCapabilityTests()
    {
        _capability = new SetSiteBoundaryMembersCapability(_chats, new SiteBoundaryMembershipService(
            Substitute.For<IRelatedSiteBuildingProvider>(), _union, Substitute.For<ICapabilitySettingsReader>(),
            Substitute.For<ILogger<SiteBoundaryMembershipService>>()));

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
        IncludedNames(result).Should().Equal("Burjman Office Tower");
        result.Output!.RootElement.GetProperty("additionalPolygons").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task AnEmptyList_IsTheSiteAlone()
    {
        var result = await RunAsync("""{"memberIds":[]}""");

        IncludedNames(result).Should().BeEmpty();
        result.Output!.RootElement.GetProperty("areaSquareMeters").GetDouble()
            .Should().BeApproximately(GeometryMath.AreaSquareMeters(Mall), 1);
    }

    [Fact]
    public async Task ByName_MatchesTheStationAndAMistypedTower()
    {
        var result = await RunAsync("""{"memberNames":["the metro station","Burjuman Office Tower"]}""");

        result.Succeeded.Should().BeTrue(result.FailureReason);
        IncludedNames(result).Should().BeEquivalentTo("Burjman Office Tower", "BurJuman Metro Station");
    }

    [Fact]
    public async Task AnUnknownName_FailsAndListsTheBuildingsThatExist()
    {
        var result = await RunAsync("""{"memberNames":["Dubai Frame"]}""");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("\"Dubai Frame\"").And.Contain("BurJuman Business Tower");
    }

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

    private async Task<AgentToolResult> RunAsync(string input)
    {
        using var document = JsonDocument.Parse(input);
        return await _capability.ExecuteAsync(
            new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), _chat.Id), document);
    }

    private static IEnumerable<string?> IncludedNames(AgentToolResult result) =>
        result.Output!.RootElement.GetProperty("includedBuildings").EnumerateArray().Select(e => e.GetString());
}
