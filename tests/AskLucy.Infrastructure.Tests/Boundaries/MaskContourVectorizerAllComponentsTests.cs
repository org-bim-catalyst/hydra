using AskLucy.Application.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// specs/053-rendered-building-footprints research D2/D4/D5 — <see cref="MaskContourVectorizer.AllComponents"/>
/// and <see cref="MaskContourVectorizer.ExtractAllRings"/>, the additive many-polygon entry point
/// this feature adds beside the existing single-largest-ring path. Synthetic masks only, no
/// fixtures — a failure here points at the component-selection/vectorisation logic itself, not at
/// image I/O (mirroring <see cref="MaskContourVectorizerTests"/>'s own convention).
/// </summary>
public sealed class MaskContourVectorizerAllComponentsTests
{
    /// <summary>Identity-mapped bounds so a pixel ring's geo coordinates equal its pixel
    /// coordinates directly (longitude = x, latitude = height - y) — convenient for assertions that
    /// only care about ring shape/count, not real-world coordinates.</summary>
    private static SatelliteImage IdentityBounds(int width, int height) =>
        new([], "image/png", West: 0, South: 0, East: width, North: height);

    private static void FillSquare(bool[,] mask, int originX, int originY, int size)
    {
        for (var x = originX; x < originX + size; x++)
        {
            for (var y = originY; y < originY + size; y++)
            {
                mask[x, y] = true;
            }
        }
    }

    [Fact]
    public void AllComponents_ShouldReturnOneEntryPerSeparatedBlob()
    {
        var mask = new bool[60, 20];
        FillSquare(mask, 0, 0, 10);
        FillSquare(mask, 20, 0, 10);
        FillSquare(mask, 40, 0, 10);

        var components = MaskContourVectorizer.AllComponents(mask, 60, 20, minimumPixelArea: 1, eightConnected: true);

        components.Should().HaveCount(3, "three separated 10x10 blobs must each become their own component");
        components.Should().OnlyContain(c => c.Count == 100);
    }

    [Fact]
    public void AllComponents_ShouldExcludeABlobBelowTheMinimumArea()
    {
        var mask = new bool[30, 10];
        FillSquare(mask, 0, 0, 10); // 100 px, kept
        for (var x = 20; x < 22; x++) mask[x, 0] = true; // 2 px speck, excluded

        var components = MaskContourVectorizer.AllComponents(mask, 30, 10, minimumPixelArea: 50, eightConnected: true);

        components.Should().ContainSingle().Which.Count.Should().Be(100);
    }

    [Fact]
    public void ExtractAllRings_ShouldCountDegenerateComponents_RatherThanSilentlyDroppingThem()
    {
        var mask = new bool[30, 10];
        FillSquare(mask, 0, 0, 10); // real ring
        mask[20, 5] = true; // single pixel: below minimumPixelArea=1 it survives AllComponents,
                            // but traces to a degenerate (< 3 point) ring after simplification is
                            // still possible for pathological shapes — asserted via the count path
                            // regardless, since a real single-pixel "ring" is 4 grid-edge corners
                            // that collapse under simplification to fewer than 3 points only in
                            // edge cases; the assertion below is on the RESULT SHAPE (counted, not
                            // silently dropped), not on this particular mask forcing degeneracy.

        var result = MaskContourVectorizer.ExtractAllRings(
            mask, 30, 10, IdentityBounds(30, 10), minimumPixelArea: 1, simplifyEpsilon: 3.0, eightConnected: true);

        // Every component AllComponents finds either becomes a usable ring or is counted as
        // degenerate — the two numbers must always add up, so nothing vanishes silently (FR-009).
        var totalComponents = MaskContourVectorizer.AllComponents(mask, 30, 10, minimumPixelArea: 1, eightConnected: true).Count;
        (result.Rings.Count + result.DegenerateCount).Should().Be(totalComponents);
    }

    [Fact]
    public void ExtractAllRings_ShouldMarkAComponentTouchingTheTileEdge()
    {
        var mask = new bool[20, 20];
        FillSquare(mask, 0, 0, 10);   // touches the tile edge (x=0, y=0)
        FillSquare(mask, 14, 14, 5);  // interior, does not touch (max index 18 < 19)

        var result = MaskContourVectorizer.ExtractAllRings(
            mask, 20, 20, IdentityBounds(20, 20), minimumPixelArea: 1, simplifyEpsilon: 0.5, eightConnected: true);

        result.Rings.Should().HaveCount(2);
        result.Rings.Should().ContainSingle(r => r.TouchesTileEdge);
        result.Rings.Should().ContainSingle(r => !r.TouchesTileEdge);
    }

    [Fact]
    public void Connectivity_DiagonallyTouchingSquares_ShouldStaySeparateUnder4Connectivity_ButMergeUnder8()
    {
        // research D4 — the exact failure this connectivity choice guards against: two buildings
        // sharing only a corner in the rendering must not become one giant shadow-caster.
        var mask = new bool[20, 20];
        FillSquare(mask, 0, 0, 5);  // occupies (0..4, 0..4)
        FillSquare(mask, 5, 5, 5);  // occupies (5..9, 5..9) — touches the first only at the corner (4,4)-(5,5)

        var fourConnected = MaskContourVectorizer.AllComponents(mask, 20, 20, minimumPixelArea: 1, eightConnected: false);
        var eightConnected = MaskContourVectorizer.AllComponents(mask, 20, 20, minimumPixelArea: 1, eightConnected: true);

        fourConnected.Should().HaveCount(2, "diagonally-touching buildings must stay separate under 4-connectivity");
        eightConnected.Should().HaveCount(1, "8-connectivity merges anything touching even diagonally — correct for one boundary, wrong for buildings");
    }

    [Fact]
    public void Connectivity_StatedLimitation_TwoSquaresSharingAFullEdge_MergeUnderEitherConnectivity()
    {
        // The stated limitation from contracts/rendered-footprint-provider.md: buildings that share
        // a WALL (a full pixel edge, not just a corner) merge regardless of connectivity, because
        // they are genuinely one connected blob in the rendering. No morphological erosion is
        // attempted to split them (research D4 — it would chamfer every non-grid-aligned corner).
        var mask = new bool[20, 20];
        FillSquare(mask, 0, 0, 5);  // (0..4, 0..4)
        FillSquare(mask, 5, 0, 5);  // (5..9, 0..4) — shares the full edge x=4|x=5, y in [0,4]

        var fourConnected = MaskContourVectorizer.AllComponents(mask, 20, 20, minimumPixelArea: 1, eightConnected: false);

        fourConnected.Should().ContainSingle("a shared full edge merges the two blocks even at 4-connectivity — this is the stated limitation, asserted so it stays honest");
    }

    [Fact]
    public void TryExtractRing_ShouldStillReturnTheSamePlainRectangleRing_AfterTheAllComponentsRefactor()
    {
        // T008 — the regression guard for T004/T005's refactor. TryExtractRing has two shipped
        // callers (the boundary extractor and the Gemini segmentation path) that this feature must
        // not disturb; this asserts its behaviour is unchanged by exercising it exactly as
        // MaskContourVectorizerTests's own "plain rectangle" case does.
        var mask = new bool[16, 10];
        for (var x = 3; x < 13; x++)
        {
            for (var y = 2; y < 8; y++)
            {
                mask[x, y] = true;
            }
        }

        var ring = MaskContourVectorizer.TryExtractRing(mask, 16, 10, IdentityBounds(16, 10));

        ring.Should().NotBeNull();
        // A closed ring around a plain 10x6 rectangle, simplified: 4 corners plus the closing
        // duplicate of the first point (ToGeoRing's own closing-vertex behaviour), unchanged.
        ring!.Count.Should().Be(5);
    }
}
