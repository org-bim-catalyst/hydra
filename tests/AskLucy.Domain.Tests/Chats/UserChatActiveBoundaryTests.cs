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

    /// <summary>
    /// specs/044-location-viewer-regression T013 (FR-009a, contract S-1/S-4) — a stored boundary
    /// must never outlive the site it names, and clearing it must never disturb the location,
    /// which is the mandatory outcome that survives every boundary failure.
    /// </summary>
    [Fact]
    public void ClearActiveBoundary_ShouldRemoveTheBoundaryAndStampTheActor()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218, SamplePolygon, 15_000, 0.9,
            BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", "user-1");
        chat.ActiveBoundary.Should().NotBeNull();

        chat.ClearActiveBoundary("system:location-resolution");

        chat.ActiveBoundary.Should().BeNull();
        chat.ModifiedBy.Should().Be("system:location-resolution");
        chat.ModifiedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ClearActiveBoundary_ShouldLeaveTheActiveLocationIntact()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveLocation(25.24, 55.30, "Zabeel Park", 0.88, "user-1");
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218, SamplePolygon, 15_000, 0.9,
            BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", "user-1");

        chat.ClearActiveBoundary("user-1");

        chat.ActiveBoundary.Should().BeNull();
        chat.ActiveLocation.Should().NotBeNull();
        chat.ActiveLocation!.LocationName.Should().Be("Zabeel Park");
    }

    [Fact]
    public void ClearActiveBoundary_ShouldBeSafe_WhenNoBoundaryIsSet()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");

        var act = () => chat.ClearActiveBoundary("user-1");

        act.Should().NotThrow();
        chat.ActiveBoundary.Should().BeNull();
    }
}
