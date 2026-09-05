using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// Exercises <see cref="RedOutlineVectorizer"/>'s pixel-space algorithms directly against
/// hand-built boolean grids — no image encoding/decoding involved, so a failure here points
/// straight at the tracing/simplification logic rather than at image I/O.
/// </summary>
public sealed class RedOutlineVectorizerTests
{
    /// <summary>A filled 10x6 rectangle: cells (0..9, 0..5).</summary>
    private static HashSet<(int X, int Y)> Rectangle(int width = 10, int height = 6)
    {
        var cells = new HashSet<(int X, int Y)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                cells.Add((x, y));
            }
        }
        return cells;
    }

    [Fact]
    public void LargestComponent_ShouldPickTheBiggerBlob_OverASmallIconLikeSpeck()
    {
        var mask = new bool[50, 50];
        // The "boundary": a large ring-like spread of pixels.
        foreach (var (x, y) in Rectangle(30, 20))
        {
            mask[x, y] = true;
        }
        // A small "icon": a 3x3 speck far away, well under the size floor.
        for (var x = 45; x < 48; x++)
        {
            for (var y = 45; y < 48; y++)
            {
                mask[x, y] = true;
            }
        }

        var component = RedOutlineVectorizer.LargestComponent(mask, 50, 50);

        component.Should().NotBeNull();
        component!.Count.Should().Be(30 * 20);
    }

    [Fact]
    public void LargestComponent_ShouldReturnNull_WhenOnlyIconSizedSpecksExist()
    {
        var mask = new bool[50, 50];
        for (var x = 10; x < 13; x++)
        {
            for (var y = 10; y < 13; y++)
            {
                mask[x, y] = true;
            }
        }

        var component = RedOutlineVectorizer.LargestComponent(mask, 50, 50);

        component.Should().BeNull("a 3x3 speck is far below the boundary-plausible size floor");
    }

    [Fact]
    public void TraceOuterRing_ShouldProduceAFourCornerLoop_ForAPlainRectangle()
    {
        var cells = Rectangle(10, 6);

        var ring = RedOutlineVectorizer.TraceOuterRing(cells, 12, 8);

        ring.Should().NotBeNull();
        // A rectangle's grid-edge trace has exactly 4 direction changes -> 4 corners before
        // simplification collapses collinear points; verify via simplification directly.
        var simplified = RedOutlineVectorizer.DouglasPeucker(ring!, epsilon: 0.5);
        simplified.Should().HaveCount(4, "a plain rectangle should never gain spurious corners");
    }

    [Fact]
    public void TraceOuterRing_ThenSimplify_ShouldPreserveAChamferedCorner()
    {
        // The exact real-world failure mode: a rectangle with one corner cut off diagonally,
        // mirroring the Al Safa Park 2 notch the coordinate-JSON prompt kept missing.
        var cells = Rectangle(20, 12);
        // Chamfer the top-left corner: remove an increasingly larger step each row near (0,0).
        cells.Remove((0, 0)); cells.Remove((1, 0)); cells.Remove((2, 0));
        cells.Remove((0, 1)); cells.Remove((1, 1));
        cells.Remove((0, 2));

        var ring = RedOutlineVectorizer.TraceOuterRing(cells, 22, 14);
        ring.Should().NotBeNull();

        var simplified = RedOutlineVectorizer.DouglasPeucker(ring!, epsilon: 0.5);

        // A plain rectangle simplifies to 4 corners; the chamfer must add at least one more
        // vertex that survives simplification.
        simplified.Count.Should().BeGreaterThan(4, "the chamfered corner must survive simplification as extra vertices");
    }

    [Fact]
    public void TraceOuterRing_ShouldPickTheLargerLoop_WhenTheShapeIsAThickRingWithAnInnerHole()
    {
        // A thick square "stroke": a 20x20 filled square with an 8x8 hole cut from its centre,
        // mirroring a drawn outline that has real width (outer edge + inner edge = two loops).
        var cells = Rectangle(20, 20);
        for (var x = 6; x < 14; x++)
        {
            for (var y = 6; y < 14; y++)
            {
                cells.Remove((x, y));
            }
        }

        var ring = RedOutlineVectorizer.TraceOuterRing(cells, 22, 22);

        ring.Should().NotBeNull();
        var simplified = RedOutlineVectorizer.DouglasPeucker(ring!, epsilon: 0.5);
        // The outer loop (20x20) encloses far more area than the inner loop (8x8) - confirm the
        // outer one won by checking the simplified ring's bounding span, not just count.
        var xs = simplified.Select(p => p.X).ToList();
        var ys = simplified.Select(p => p.Y).ToList();
        (xs.Max() - xs.Min()).Should().Be(20, "the outer edge of the stroke, not the inner hole, must be selected");
        (ys.Max() - ys.Min()).Should().Be(20);
    }

    [Fact]
    public void DouglasPeucker_ShouldCollapseANearlyStraightEdge_ToItsTwoEndpoints()
    {
        // A closed diamond with a lot of near-collinear staircase noise along one edge, the kind
        // TraceOuterRing produces from a real grid trace.
        var ring = new List<(int X, int Y)>
        {
            (0, 0), (1, 0), (2, 0), (3, 0), (4, 0),
            (4, 4),
            (0, 4),
            (0, 3), (0, 2), (0, 1),
        };

        var simplified = RedOutlineVectorizer.DouglasPeucker(ring, epsilon: 0.5);

        simplified.Should().HaveCount(4);
        simplified.Should().Contain((0, 0)).And.Contain((4, 0)).And.Contain((4, 4)).And.Contain((0, 4));
    }
}
