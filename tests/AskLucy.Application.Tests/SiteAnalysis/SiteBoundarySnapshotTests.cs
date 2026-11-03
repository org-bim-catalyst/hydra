using AskLucy.Application.SiteAnalysis;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist data-model.md § 1 — the boundary snapshot parser tolerates both
/// serializer casing forms and never throws: a missing snapshot and an unreadable one are different
/// stated reasons, not an empty ring.
/// </summary>
public sealed class SiteBoundarySnapshotTests
{
    [Fact]
    public void TryParse_ShouldReadThePascalCaseSnapshotWrittenByDispatch()
    {
        const string json = """
            [
              { "Latitude": 25.1560, "Longitude": 55.2210 },
              { "Latitude": 25.1560, "Longitude": 55.2220 },
              { "Latitude": 25.1550, "Longitude": 55.2220 }
            ]
            """;

        var result = SiteBoundarySnapshot.TryParse(json);

        result.HasRing.Should().BeTrue();
        result.Ring.Should().HaveCount(3);
        result.Ring![0].Latitude.Should().Be(25.1560);
        result.UnavailableReason.Should().BeNull();
    }

    [Fact]
    public void TryParse_ShouldAcceptCamelCase()
    {
        const string json = """
            [
              { "latitude": 25.1560, "longitude": 55.2210 },
              { "latitude": 25.1560, "longitude": 55.2220 },
              { "latitude": 25.1550, "longitude": 55.2220 }
            ]
            """;

        SiteBoundarySnapshot.TryParse(json).Ring.Should().HaveCount(3);
    }

    [Fact]
    public void TryParse_ShouldAcceptAClosedRing()
    {
        const string json = """
            [
              { "Latitude": 25.1560, "Longitude": 55.2210 },
              { "Latitude": 25.1560, "Longitude": 55.2220 },
              { "Latitude": 25.1550, "Longitude": 55.2220 },
              { "Latitude": 25.1560, "Longitude": 55.2210 }
            ]
            """;

        SiteBoundarySnapshot.TryParse(json).Ring.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_ShouldReportNoBoundary_WhenTheSnapshotIsAbsent(string? json)
    {
        var result = SiteBoundarySnapshot.TryParse(json);

        result.HasRing.Should().BeFalse();
        result.UnavailableReason.Should().Be(SiteBoundarySnapshot.MissingReason);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[{ "Latitude": 25.1, "Longitude": 55.2 }, { "Latitude": 25.2, "Longitude": 55.3 }]""")]
    [InlineData("""[{ "Latitude": "25.1", "Longitude": 55.2 }, { "Latitude": 25.2, "Longitude": 55.3 }, { "Latitude": 25.3, "Longitude": 55.4 }]""")]
    [InlineData("""[1, 2, 3]""")]
    public void TryParse_ShouldReportUnreadable_WhenTheSnapshotCannotBeUsed(string json)
    {
        var result = SiteBoundarySnapshot.TryParse(json);

        result.HasRing.Should().BeFalse();
        result.UnavailableReason.Should().Be(SiteBoundarySnapshot.UnreadableReason);
    }
}
