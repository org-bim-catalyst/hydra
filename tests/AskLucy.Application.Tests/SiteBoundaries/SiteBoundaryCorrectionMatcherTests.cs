using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/079 FR-023, FR-026 - a correction is reused for the same site in the same place, only ever for its owner.</summary>
public sealed class SiteBoundaryCorrectionMatcherTests
{
    private const double Lat = 23.59;
    private const double Lon = 58.40;
    private const double MetersPerDegreeLatitude = 111_320;

    private readonly ISiteBoundaryCorrectionRepository _repository = Substitute.For<ISiteBoundaryCorrectionRepository>();

    private SiteBoundaryCorrectionMatcher Create() => new(_repository);

    private static GeoPoint At(double eastMeters, double northMeters) =>
        new(Lat + (northMeters / MetersPerDegreeLatitude), Lon + (eastMeters / (MetersPerDegreeLatitude * Math.Cos(Lat * Math.PI / 180))));

    /// <summary>A 200 x 200 m found outline centred on (0,0), with a correction saved for it.</summary>
    private static SiteBoundaryCorrection CorrectionFor(string userId, string siteName = "Muscat Grand Mall")
    {
        IReadOnlyList<GeoPoint> found = [At(-100, -100), At(100, -100), At(100, 100), At(-100, 100), At(-100, -100)];
        var centre = At(0, 0);
        return SiteBoundaryCorrection.Create(
            userId, siteName, centre.Latitude, centre.Longitude,
            new FoundSiteBoundarySnapshot(found, [], null, 40_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [found], 35_000, [], userId);
    }

    private void Stored(params SiteBoundaryCorrection[] corrections) =>
        _repository.FindCandidatesAsync("user-1", "muscat grand mall", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SiteBoundaryCorrection>>(corrections));

    [Fact]
    public async Task ThePointInsideTheFoundOutline_Matches()
    {
        var correction = CorrectionFor("user-1");
        Stored(correction);

        var match = await Create().FindAsync("user-1", "Muscat Grand Mall", At(20, 20), TestContext.Current.CancellationToken);

        match.Should().BeSameAs(correction);
    }

    [Fact]
    public async Task ThePointWithin250mOfTheCentre_Matches_EvenOutsideTheOutline()
    {
        Stored(CorrectionFor("user-1"));

        (await Create().FindAsync("user-1", "Muscat Grand Mall", At(200, 0), TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task ThePointJustBeyondTheOutlineGrownBy100m_ButFarFromTheCentre_Matches()
    {
        // 300 m east of the centre is 200 m past the outline's edge - beyond both rules.
        Stored(CorrectionFor("user-1"));

        (await Create().FindAsync("user-1", "Muscat Grand Mall", At(300, 0), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task TheSameNameKilometresAway_DoesNotMatch()
    {
        Stored(CorrectionFor("user-1"));

        (await Create().FindAsync("user-1", "Muscat Grand Mall", At(5_000, 0), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task NameVariants_AreLookedUpNormalised()
    {
        Stored(CorrectionFor("user-1"));

        (await Create().FindAsync("user-1", "  MUSCAT   grand mall ", At(0, 0), TestContext.Current.CancellationToken)).Should().NotBeNull();
        await _repository.Received().FindCandidatesAsync("user-1", "muscat grand mall", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADifferentUser_NeverMatches_BecauseTheLookupIsScopedToThem()
    {
        Stored(CorrectionFor("user-1"));

        var match = await Create().FindAsync("user-2", "Muscat Grand Mall", At(0, 0), TestContext.Current.CancellationToken);

        match.Should().BeNull();
        await _repository.DidNotReceive().FindCandidatesAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoCandidates_PicksTheNearerCentre()
    {
        var near = CorrectionFor("user-1");
        var far = CorrectionFor("user-1");
        // Same name, saved for a site whose found centre is 200 m east: still "the same site", but farther.
        var moved = At(200, 0);
        IReadOnlyList<GeoPoint> ring = [At(100, -100), At(300, -100), At(300, 100), At(100, 100), At(100, -100)];
        far = SiteBoundaryCorrection.Create(
            "user-1", "Muscat Grand Mall", moved.Latitude, moved.Longitude,
            new FoundSiteBoundarySnapshot(ring, [], null, 40_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "x", []),
            [ring], 35_000, [], "user-1");
        Stored(far, near);

        var match = await Create().FindAsync("user-1", "Muscat Grand Mall", At(10, 0), TestContext.Current.CancellationToken);

        match.Should().BeSameAs(near);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankNameOrUser_MatchesNothingAndAsksNothing(string blank)
    {
        (await Create().FindAsync(blank, "Muscat Grand Mall", At(0, 0), TestContext.Current.CancellationToken)).Should().BeNull();
        (await Create().FindAsync("user-1", blank, At(0, 0), TestContext.Current.CancellationToken)).Should().BeNull();
        await _repository.DidNotReceiveWithAnyArgs().FindCandidatesAsync(default!, default!, default);
    }
}
