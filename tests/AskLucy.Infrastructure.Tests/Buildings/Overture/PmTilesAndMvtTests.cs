using AskLucy.Infrastructure.Buildings.Overture;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings.Overture;

/// <summary>specs/075 — the PMTiles directory and MVT decoding the Overture provider stands on.</summary>
public sealed class PmTilesAndMvtTests
{
    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]
    public void TileId_ShouldFollowTheHilbertCurve_AfterAllLowerZooms(int zoom, int x, int y, ulong expected)
    {
        // The PMTiles v3 specification's own worked examples.
        PmTiles.TileId(zoom, x, y).Should().Be(expected);
    }

    [Fact]
    public void ParseDirectory_ShouldRoundTrip_BothOffsetForms()
    {
        PmTilesEntry[] entries =
        [
            new(10, 0, 100, 1),
            new(11, 100, 50, 3), // contiguous — written as 0
            new(20, 900, 7, 1),  // not contiguous — written as offset + 1
        ];

        PmTiles.ParseDirectory(OvertureTestBucket.Directory(entries)).Should().Equal(entries);
    }

    [Fact]
    public void Find_ShouldHonourRunLengths_AndLeafPointers()
    {
        PmTilesEntry[] entries = [new(10, 0, 100, 3), new(20, 100, 50, 0)];

        PmTiles.Find(entries, 9).Should().BeNull("nothing starts at or before tile 9");
        PmTiles.Find(entries, 12)!.Value.TileId.Should().Be(10, "tile 12 is inside the run 10..12");
        PmTiles.Find(entries, 13).Should().BeNull("the run ends at 12");
        PmTiles.Find(entries, 500)!.Value.RunLength.Should().Be(0, "a leaf pointer covers every id after it");
    }

    [Fact]
    public void Parse_ShouldDecodePolygonsAndProperties_FromEachLayer()
    {
        var tile = VectorTileWriter.Tile(
            ("building", [new TestBuilding(
                [[(0, 0), (10, 0), (10, 10), (0, 10)], [(2, 2), (2, 8), (8, 8), (8, 2)]],
                new Dictionary<string, object> { ["id"] = "b1", ["height"] = 12.5, ["num_floors"] = 4, ["is_underground"] = false })]),
            ("building_part", [new TestBuilding([[(0, 0), (5, 0), (5, 5), (0, 5)]], new Dictionary<string, object> { ["building_id"] = "b1" })]));

        var layers = MvtTile.Parse(tile);

        layers.Select(l => l.Name).Should().Equal("building", "building_part");
        var building = layers[0].Features.Should().ContainSingle().Subject;
        layers[0].Extent.Should().Be(4096);
        building.Properties["id"].Should().Be("b1");
        building.Properties["height"].Should().Be(12.5);
        building.Properties["num_floors"].Should().Be(4L);
        building.Properties["is_underground"].Should().Be(false);
        building.Rings.Should().HaveCount(2);
        building.Rings[0].Points.Should().Equal((0, 0), (10, 0), (10, 10), (0, 10));
        building.Rings[1].Points.Should().Equal((2, 2), (2, 8), (8, 8), (8, 2));
    }

    [Fact]
    public void SignedArea_ShouldBePositiveForAnExterior_AndNegativeForAHole()
    {
        new MvtRing([(0, 0), (10, 0), (10, 10), (0, 10)]).SignedArea().Should().Be(100);
        new MvtRing([(2, 2), (2, 8), (8, 8), (8, 2)]).SignedArea().Should().Be(-36);
    }

    [Fact]
    public void ClipToTile_ShouldCutARingCrossingTheEdge_AtTheEdge()
    {
        var clipped = OvertureBuildingFootprintProvider.ClipToTile([(4000, 100), (4200, 100), (4200, 200), (4000, 200)], 4096);

        clipped.Should().HaveCount(4);
        clipped.Max(p => p.X).Should().Be(4096);
        clipped.Min(p => p.X).Should().Be(4000);
    }

    [Fact]
    public void ClipToTile_ShouldLeaveNothing_OfARingEntirelyInTheBuffer()
    {
        OvertureBuildingFootprintProvider.ClipToTile([(-60, 100), (-10, 100), (-10, 200), (-60, 200)], 4096)
            .Should().BeEmpty();
    }
}
