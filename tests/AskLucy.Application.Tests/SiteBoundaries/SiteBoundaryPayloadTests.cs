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
    public void Write_NamesTheIncludedAndExcludedBuildingsForTheModelToSay()
    {
        using var written = SiteBoundaryPayload.Write(Boundary(Members));
        var root = written.RootElement;

        root.GetProperty("includedBuildings").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("BurJuman Business Tower", "BurJuman Arjaan by Rotana");
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
    }
}
