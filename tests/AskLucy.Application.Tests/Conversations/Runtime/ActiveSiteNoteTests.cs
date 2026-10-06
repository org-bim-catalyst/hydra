using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>specs/079 FR-021 - the line telling Lucy which outline is on screen and how big it is.</summary>
public sealed class ActiveSiteNoteTests
{
    private static readonly IReadOnlyList<GeoPoint> Ring = [new(25.156, 55.221), new(25.156, 55.222), new(25.155, 55.222)];

    private static ActiveSiteBoundary Found(double area = 48_860) =>
        new("Muscat Grand Mall", 25.1555, 55.2215, Ring, area, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap");

    private static ActiveSiteBoundary HandEdited(double area = 35_210) =>
        Found(area) with { Source = SiteBoundarySource.UserCorrected, ConfidenceLevel = BoundaryConfidenceLevel.High };

    private static TurnContext ContextWith(ActiveSiteBoundary? boundary) =>
        new("user-1", Guid.NewGuid(), null, boundary, [], false, true, 0, new HashSet<AgentToolPermission>(), null);

    [Fact]
    public void ForAnOutlineAsFound_NamesTheSiteConfidenceSourceAndArea()
    {
        var note = ActiveSiteNote.Describe(ContextWith(Found()));

        note.Should().Contain("Muscat Grand Mall").And.Contain("Medium").And.Contain("OsmBoundary").And.Contain("48,860 m");
        note.Should().NotContain("hand-edited");
    }

    [Fact]
    public void ForAHandEditedOutline_SaysTheUserShapedItAndGivesTheEditedArea()
    {
        var note = ActiveSiteNote.Describe(ContextWith(HandEdited()));

        note.Should().Contain("hand-edited").And.Contain("35,210 m").And.Contain("current outline");
        note.Should().Contain("out of date", "the found outline's figures must not read as current");
        note.Should().NotContain("48,860");
    }

    [Fact]
    public void ForAHandEditedOutlineWithVoids_SaysHowManyAndThatTheAreaLeavesThemOut()
    {
        IReadOnlyList<GeoPoint> atrium = [new(25.1558, 55.2214), new(25.1558, 55.2216), new(25.1556, 55.2216)];

        var note = ActiveSiteNote.Describe(ContextWith(HandEdited() with { Voids = [[atrium, atrium]] }));

        note.Should().Contain("2 voids").And.Contain("leaves out");
        ActiveSiteNote.Describe(ContextWith(HandEdited() with { Voids = [[atrium]] })).Should().Contain("1 void ");
    }

    [Fact]
    public void WithoutVoids_TheNoteSaysNothingOfThem() =>
        ActiveSiteNote.Describe(ContextWith(HandEdited())).Should().NotContain("void");

    [Fact]
    public void WithoutAnOutline_ThereIsNoNote()
    {
        ActiveSiteNote.Describe(ContextWith(null)).Should().BeNull();
        ActiveSiteNote.Describe((ActiveSiteBoundary?)null).Should().BeNull();
    }

    [Fact]
    public void TheAreaIsFormattedTheSameWhateverTheMachinesCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            ActiveSiteNote.Describe(HandEdited(1_234_567)).Should().Contain("1,234,567 m");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
