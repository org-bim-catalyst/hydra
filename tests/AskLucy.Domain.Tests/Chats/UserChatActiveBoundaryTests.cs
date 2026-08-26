using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

/// <summary>
/// specs/042-site-boundary-resolution — <see cref="UserChat.SetActiveBoundary"/>, mirroring
/// the existing <c>SetActiveLocation</c> test coverage pattern. Persistence round-trip (including
/// the polygon JSON conversion) is covered separately in
/// <c>AskLucy.Persistence.Tests.Chats.UserChatActiveBoundaryPersistenceTests</c>.
/// </summary>
public sealed class UserChatActiveBoundaryTests
{
    private static readonly IReadOnlyList<GeoPoint> SamplePolygon =
    [
        new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220),
    ];

    [Fact]
    public void ActiveBoundary_ShouldBeNull_UntilSet()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.ActiveBoundary.Should().BeNull();
    }

    [Fact]
    public void SetActiveBoundary_ShouldPopulateAllFieldsAndModifiedAudit()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");

        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218, SamplePolygon, 15_000,
            0.92, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary,
            "OpenStreetMap (leisure=park)", "user-1");

        chat.ActiveBoundary.Should().NotBeNull();
        chat.ActiveBoundary!.SiteName.Should().Be("Al Safa Park 2");
        chat.ActiveBoundary.CentroidLatitude.Should().Be(25.156);
        chat.ActiveBoundary.Polygon.Should().BeEquivalentTo(SamplePolygon);
        chat.ActiveBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        chat.ActiveBoundary.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        chat.ModifiedBy.Should().Be("user-1");
        chat.ModifiedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SetActiveBoundary_ShouldReplaceWholesale_WhenCalledAgainForADifferentSite()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218, SamplePolygon, 15_000,
            0.92, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap", "user-1");

        chat.SetActiveBoundary(
            "Zabeel Park", 25.24, 55.30, SamplePolygon, 20_000,
            0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.ManualFallback, "approximate buffer", "user-1");

        chat.ActiveBoundary!.SiteName.Should().Be("Zabeel Park");
        chat.ActiveBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.Medium);
        chat.ActiveBoundary.Source.Should().Be(SiteBoundarySource.ManualFallback);
    }
}
