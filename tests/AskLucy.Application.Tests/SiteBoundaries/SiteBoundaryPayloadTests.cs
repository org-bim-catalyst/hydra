using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/077 — the tool result both boundary capabilities return, and the stream reads back.</summary>
public sealed class SiteBoundaryPayloadTests
{
    [Fact]
    public void Read_GivesBackWhatWriteWrote()
    {
        var boundary = Boundary(Members) with
        {
            CorePolygon = Mall,
            AdditionalPolygons = [TowerAcrossTheStreet.Ring],
        };

        using var written = SiteBoundaryPayload.Write(boundary);
        var read = SiteBoundaryPayload.Read(written.RootElement);

        read.SiteName.Should().Be(boundary.SiteName);
        read.Polygon.Should().Equal(boundary.Polygon);
        read.CorePolygon.Should().Equal(Mall);
        read.AdditionalPolygons.Should().ContainSingle().Which.Should().Equal(TowerAcrossTheStreet.Ring);
        read.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        read.SourceDetail.Should().Be("osm_way_100");
        read.Members.Select(m => (m.Id, m.Kind, m.Relation, m.Included)).Should().Equal(
            boundary.Members.Select(m => (m.Id, m.Kind, m.Relation, m.Included)));
    }

    [Fact]
    public void Voids_RoundTrip_AndTheModelIsToldHowManyAndHowMuchTheyTakeOut()
    {
        IReadOnlyList<GeoPoint> atrium = [new(25.1558, 55.2214), new(25.1558, 55.2216), new(25.1556, 55.2216)];
        var boundary = Boundary(Members) with { Voids = [[atrium]] };

        using var written = SiteBoundaryPayload.Write(boundary);
        var read = SiteBoundaryPayload.Read(written.RootElement);

        read.Voids.Should().HaveCount(1);
        read.Voids[0].Should().ContainSingle().Which.Should().Equal(atrium);
        written.RootElement.GetProperty("voidCount").GetInt32().Should().Be(1);
        written.RootElement.GetProperty("voidAreaSquareMeters").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public void WithoutVoids_TheModelIsToldThereAreNone_AndAnOldPayloadReadsAsHavingNone()
    {
        using var written = SiteBoundaryPayload.Write(Boundary(Members));
        written.RootElement.GetProperty("voidCount").GetInt32().Should().Be(0);
        SiteBoundaryPayload.Read(written.RootElement).Voids.Should().BeEmpty();

        var withoutField = System.Text.Json.Nodes.JsonNode.Parse(written.RootElement.GetRawText())!.AsObject();
        withoutField.Remove("voids");
        using var old = System.Text.Json.JsonDocument.Parse(withoutField.ToJsonString());
        SiteBoundaryPayload.Read(old.RootElement).Voids.Should().BeEmpty();
    }

    [Fact]
    public void Write_NamesWhatTheOutlineCoversAndWhatItLeavesOut_ForTheModelToSay()
    {
        using var written = SiteBoundaryPayload.Write(Boundary(Members));
        var root = written.RootElement;

        root.GetProperty("outlineCovers").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("BurJuman Mall", "BurJuman Business Tower", "BurJuman Arjaan by Rotana");
        root.GetProperty("excludedBuildings").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Burjman Office Tower", "BurJuman Metro Station");
    }

    [Fact]
    public void Read_AcceptsAPayloadFromBeforeMembersExisted()
    {
        using var written = SiteBoundaryPayload.Write(Boundary());

        var read = SiteBoundaryPayload.Read(written.RootElement);

        read.CorePolygon.Should().BeNull();
        read.AdditionalPolygons.Should().BeEmpty();
        read.Members.Should().BeEmpty();

        // No related buildings, nothing to ask about — so nothing for the narration to list.
        written.RootElement.GetProperty("outlineCovers").GetArrayLength().Should().Be(0);
    }
}
