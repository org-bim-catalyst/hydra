using System.Buffers.Binary;
using System.Text;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Buildings.Esri;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings.Esri;

/// <summary>specs/075 — the pure I3S pieces: box distance, attribute buffers, Draco position scales, feature centres.</summary>
public sealed class I3sPrimitivesTests
{
    private static readonly GeoPoint Dubai = new(25.1560, 55.2218);

    [Fact]
    public void DistanceMetres_ShouldBeZero_InsideTheEarthSizedRootBox()
    {
        // The real layer's root: centred deep below the surface, axis-aligned, spanning the Earth.
        var root = new I3sOrientedBoundingBox(0, 0, -6_356_752, 6_400_000, 6_400_000, 6_400_000, 0, 0, 0, 1);

        root.DistanceMetres(Dubai).Should().Be(0);
    }

    [Fact]
    public void DistanceMetres_ShouldMeasureFromTheBoxEdge_AlongARotatedAxis()
    {
        // At (0°, 0°) Earth-centred X points up, Y east and Z north. A 90° turn about Z swaps the
        // box's X and Y axes, so a box 10 m wide east-west but 1000 m "tall" becomes the reverse.
        var halfTurn = Math.Sqrt(0.5);
        var box = new I3sOrientedBoundingBox(0, 0, 0, 1000, 10, 10, 0, 0, halfTurn, halfTurn);
        var fiveHundredMetresEast = new GeoPoint(0, 500 / 111_319.49);

        box.DistanceMetres(fiveHundredMetresEast).Should().Be(0, "the box's long axis now runs east-west");

        var unrotated = box with { QuaternionZ = 0, QuaternionW = 1 };
        unrotated.DistanceMetres(fiveHundredMetresEast).Should().BeApproximately(490, 1);
    }

    [Fact]
    public void ReadNumbers_ShouldReadFloat32AfterTheCount_AndFloat64AfterPadding()
    {
        I3sAttributeReader.ReadNumbers(EsriBuildingHeightSourceTests.Float32Attribute([1.5f, 317.4f]), "Float32")
            .Should().BeEquivalentTo(new[] { 1.5, 317.4 }, o => o.Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-4)).WhenTypeIs<double>());

        var float64 = new byte[8 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(float64, 2);
        BinaryPrimitives.WriteDoubleLittleEndian(float64.AsSpan(8), 123456789012);
        BinaryPrimitives.WriteDoubleLittleEndian(float64.AsSpan(16), 42);
        I3sAttributeReader.ReadNumbers(float64, "Float64").Should().Equal(123456789012, 42);
    }

    [Fact]
    public void ReadNumbers_ShouldRejectATruncatedBuffer()
    {
        var act = () => I3sAttributeReader.ReadNumbers(EsriBuildingHeightSourceTests.Float32Attribute([1f, 2f])[..8], "Float32");

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ReadStrings_ShouldReadEachValue_WithoutItsTerminator()
    {
        I3sAttributeReader.ReadStrings(EsriBuildingHeightSourceTests.StringAttribute(["Vantor", "OSM", "برج"]))
            .Should().Equal("Vantor", "OSM", "برج");
    }

    [Fact]
    public void ReadPositionScales_ShouldReadTheI3sScalesFromTheDracoMetadata()
    {
        var geometry = DracoHeader(("i3s-order", [1, 2]), ("i3s-scale_x", Double(1e-7)), ("i3s-scale_y", Double(9e-8)));

        DracoI3sGeometryDecoder.ReadPositionScales(geometry).Should().Be((1e-7, 9e-8));
    }

    [Fact]
    public void ReadPositionScales_ShouldRejectGeometryWithoutScales()
    {
        var withoutMetadata = Encoding.ASCII.GetBytes("DRACO").Concat(new byte[] { 2, 2, 1, 1, 0, 0 }).ToArray();

        FluentActions.Invoking(() => DracoI3sGeometryDecoder.ReadPositionScales(withoutMetadata)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => DracoI3sGeometryDecoder.ReadPositionScales(DracoHeader(("i3s-scale_x", Double(1e-7)))))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => DracoI3sGeometryDecoder.ReadPositionScales(DracoHeader(("i3s-scale_x", Double(1e-7)))[..20]))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FeatureCentres_ShouldBeTheCentreOfEachFeaturesBoundingBox()
    {
        float[] x = [0, 10, 10, 0, 100, 104];
        float[] y = [0, 0, 20, 20, 50, 50];
        int[] feature = [0, 0, 0, 0, 2, 2];

        var centres = DracoI3sGeometryDecoder.FeatureCentres(x, y, feature, 3);

        centres[0].Should().Be((5.0, 10.0));
        centres[1].Should().BeNull("feature 1 has no vertices");
        centres[2].Should().Be((102.0, 50.0));
    }

    private static byte[] Double(double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>"DRACO", v2.2, mesh, edgebreaker, metadata flag, then one attribute's metadata.</summary>
    private static byte[] DracoHeader(params (string Name, byte[] Value)[] entries)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("DRACO")) { 2, 2, 1, 1, 0x00, 0x80 };
        bytes.Add(1); // one attribute metadata
        bytes.Add(0); // attribute id
        bytes.Add((byte)entries.Length);
        foreach (var (name, value) in entries)
        {
            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(name));
            bytes.Add((byte)value.Length);
            bytes.AddRange(value);
        }

        bytes.Add(0); // no nested metadata
        bytes.Add(0); // geometry metadata: no entries
        bytes.Add(0);
        return [.. bytes];
    }
}
