using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.SiteBoundaries;

/// <summary>specs/079 — <see cref="SiteBoundaryCorrection"/> invariants, revision changes and name normalisation.</summary>
public sealed class SiteBoundaryCorrectionTests
{
    private static readonly IReadOnlyList<GeoPoint> Ring =
    [
        new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220),
    ];

    private static readonly FoundSiteBoundarySnapshot Snapshot = new(
        Ring, [], null, 15_000, 0.8, BoundaryConfidenceLevel.Medium,
        SiteBoundarySource.OsmBoundary, "OpenStreetMap", []);

    private static SiteBoundaryCorrection NewCorrection() =>
        SiteBoundaryCorrection.Create("user-1", "Muscat Grand Mall", 25.156, 55.221, Snapshot, [Ring], 14_000, [], "user-1");

    [Fact]
    public void Create_ShouldPopulateFieldsAndAssignARevision()
    {
        var correction = NewCorrection();

        correction.UserId.Should().Be("user-1");
        correction.NormalizedSiteName.Should().Be("muscat grand mall");
        correction.EditedRings.Should().HaveCount(1);
        correction.Revision.Should().NotBe(Guid.Empty);
        correction.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectAMissingUser(string userId)
    {
        var act = () => SiteBoundaryCorrection.Create(userId, "Site", 0, 0, Snapshot, [Ring], 1, [], "a");
        act.Should().Throw<DomainRuleViolationException>();
    }

    private static readonly IReadOnlyList<GeoPoint> Void =
    [
        new(25.1558, 55.2216), new(25.1558, 55.2218), new(25.1556, 55.2218),
    ];

    [Fact]
    public void Create_ShouldKeepTheVoidsOfEachRing()
    {
        var correction = SiteBoundaryCorrection.Create(
            "user-1", "Site", 0, 0, Snapshot, [Ring], 14_000, [], "a", editedVoids: [[Void]]);

        correction.EditedVoids.Should().HaveCount(1);
        correction.EditedVoids[0].Should().ContainSingle().Which.Should().Equal(Void);
    }

    [Fact]
    public void Create_WithoutVoids_ShouldHaveNone()
    {
        NewCorrection().EditedVoids.Should().BeEmpty();
    }

    [Fact]
    public void Create_ShouldRejectVoidsForMoreRingsThanItHas()
    {
        var act = () => SiteBoundaryCorrection.Create(
            "user-1", "Site", 0, 0, Snapshot, [Ring], 1, [], "a", editedVoids: [[Void], [Void]]);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldRejectAVoidWithFewerThanThreeCorners()
    {
        IReadOnlyList<GeoPoint> twoCorners = [new(1, 1), new(2, 2)];
        var act = () => SiteBoundaryCorrection.Create(
            "user-1", "Site", 0, 0, Snapshot, [Ring], 1, [], "a", editedVoids: [[twoCorners]]);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void ReplaceRings_ShouldReplaceTheVoidsToo_AndDropThemWhenNoneAreGiven()
    {
        var correction = NewCorrection();

        correction.ReplaceRings([Ring], 13_000, "a", [[Void]]);
        correction.EditedVoids[0].Should().ContainSingle();

        correction.ReplaceRings([Ring], 14_000, "a");
        correction.EditedVoids.Should().BeEmpty();
    }

    [Fact]
    public void Create_ShouldRejectNoRings()
    {
        var act = () => SiteBoundaryCorrection.Create("user-1", "Site", 0, 0, Snapshot, [], 1, [], "a");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldRejectARingWithFewerThanThreeCorners()
    {
        IReadOnlyList<GeoPoint> twoCorners = [new(1, 1), new(2, 2)];
        var act = () => SiteBoundaryCorrection.Create("user-1", "Site", 0, 0, Snapshot, [twoCorners], 1, [], "a");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void Create_ShouldRejectANonPositiveArea(double area)
    {
        var act = () => SiteBoundaryCorrection.Create("user-1", "Site", 0, 0, Snapshot, [Ring], area, [], "a");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void ReplaceRings_ShouldChangeTheRevisionAndAudit()
    {
        var correction = NewCorrection();
        var before = correction.Revision;

        correction.ReplaceRings([Ring, Ring], 20_000, "user-2");

        correction.Revision.Should().NotBe(before);
        correction.AreaSquareMeters.Should().Be(20_000);
        correction.EditedRings.Should().HaveCount(2);
        correction.ModifiedBy.Should().Be("user-2");
    }

    [Fact]
    public void ApplyMembership_ShouldChangeTheRevisionAndRebaseTheSnapshot()
    {
        var correction = NewCorrection();
        var before = correction.Revision;
        var rebased = Snapshot with { AreaSquareMeters = 9_000 };

        correction.ApplyMembership([Ring], 8_000, [], rebased, "user-1");

        correction.Revision.Should().NotBe(before);
        correction.FoundSnapshot.AreaSquareMeters.Should().Be(9_000);
    }

    [Fact]
    public void Delete_ShouldSoftDeleteAndChangeTheRevision()
    {
        var correction = NewCorrection();
        var before = correction.Revision;

        correction.Delete("user-1");

        correction.IsDeleted.Should().BeTrue();
        correction.DeletedBy.Should().Be("user-1");
        correction.Revision.Should().NotBe(before);
    }

    [Theory]
    [InlineData("Muscat  Grand Mall", "muscat grand mall")]
    [InlineData("  MUSCAT grand\tmall ", "muscat grand mall")]
    [InlineData("Café", "cafe")]
    public void NormalizeSiteName_ShouldLowerFoldAndCollapse(string input, string expected) =>
        SiteBoundaryCorrection.NormalizeSiteName(input).Should().Be(expected);
}
