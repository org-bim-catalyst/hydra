using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// <see cref="StaticMapFraming.OffsetByPixels"/> exists specifically so tiled fetches can be
/// stitched with zero seam — these pin the property that actually matters for that: two tiles
/// offset by exactly half a tile-width must produce bounds that touch exactly, with no gap or
/// overlap, since any drift here would show up as a visible seam or a misplaced vertex in a
/// stitched extraction.
/// </summary>
public sealed class StaticMapFramingTests
{
    private static readonly GeoPoint AlSafaCenter = new(25.1558327, 55.2217644);

    [Fact]
    public void OffsetByPixels_ShouldReturnTheSamePoint_WhenTheOffsetIsZero()
    {
        var result = StaticMapFraming.OffsetByPixels(AlSafaCenter, zoom: 18, dxPixels: 0, dyPixels: 0);

        result.Latitude.Should().BeApproximately(AlSafaCenter.Latitude, 1e-9);
        result.Longitude.Should().BeApproximately(AlSafaCenter.Longitude, 1e-9);
    }

    [Fact]
    public void OffsetByPixels_ShouldProduceTouchingBounds_ForTwoTilesHalfATileWidthApart()
    {
        const int zoom = 19;
        const int halfTileWidth = StaticMapFraming.ImageSizePixels / 2;

        var left = StaticMapFraming.OffsetByPixels(AlSafaCenter, zoom, dxPixels: -halfTileWidth, dyPixels: 0);
        var right = StaticMapFraming.OffsetByPixels(AlSafaCenter, zoom, dxPixels: halfTileWidth, dyPixels: 0);

        var (_, _, leftEast, _) = StaticMapFraming.CoveredBounds(left, zoom);
        var (rightWest, _, _, _) = StaticMapFraming.CoveredBounds(right, zoom);

        leftEast.Should().BeApproximately(rightWest, 1e-9, "adjacent tiles offset by exactly one tile-width must share an edge with no gap or overlap");
    }

    [Fact]
    public void OffsetByPixels_ShouldProduceTouchingBounds_ForTwoTilesHalfATileHeightApart()
    {
        const int zoom = 19;
        const int halfTileWidth = StaticMapFraming.ImageSizePixels / 2;

        var top = StaticMapFraming.OffsetByPixels(AlSafaCenter, zoom, dxPixels: 0, dyPixels: -halfTileWidth);
        var bottom = StaticMapFraming.OffsetByPixels(AlSafaCenter, zoom, dxPixels: 0, dyPixels: halfTileWidth);

        var (_, topSouth, _, _) = StaticMapFraming.CoveredBounds(top, zoom);
        var (_, _, _, bottomNorth) = StaticMapFraming.CoveredBounds(bottom, zoom);

        topSouth.Should().BeApproximately(bottomNorth, 1e-9, "adjacent tiles offset by exactly one tile-height must share an edge with no gap or overlap");
    }

    [Fact]
    public void OffsetByPixels_TwoByTwoGrid_ShouldCoverTheSameOverallExtent_AsASingleTileOneZoomLevelLower()
    {
        const int oldZoom = 18;
        const int newZoom = oldZoom + 1;
        const int halfTileWidth = StaticMapFraming.ImageSizePixels / 2;

        var topLeft = StaticMapFraming.OffsetByPixels(AlSafaCenter, newZoom, -halfTileWidth, -halfTileWidth);
        var bottomRight = StaticMapFraming.OffsetByPixels(AlSafaCenter, newZoom, halfTileWidth, halfTileWidth);

        var (stitchedWest, _, _, stitchedNorth) = StaticMapFraming.CoveredBounds(topLeft, newZoom);
        var (_, stitchedSouth, stitchedEast, _) = StaticMapFraming.CoveredBounds(bottomRight, newZoom);

        var (oldWest, oldSouth, oldEast, oldNorth) = StaticMapFraming.CoveredBounds(AlSafaCenter, oldZoom);

        stitchedWest.Should().BeApproximately(oldWest, 1e-9);
        stitchedSouth.Should().BeApproximately(oldSouth, 1e-9);
        stitchedEast.Should().BeApproximately(oldEast, 1e-9);
        stitchedNorth.Should().BeApproximately(oldNorth, 1e-9);
    }
}
