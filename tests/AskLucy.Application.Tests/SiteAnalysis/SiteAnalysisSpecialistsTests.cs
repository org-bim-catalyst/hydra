using AskLucy.Application.SiteAnalysis;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist FR-002/FR-031 — the fan-out declaration is the single source of
/// both the published branches and the expected-result count; a specialist added here can never be
/// dispatched without being counted.
/// </summary>
public sealed class SiteAnalysisSpecialistsTests
{
    [Fact]
    public void All_ShouldDeclareTheGeometrySpecialist()
    {
        SiteAnalysisSpecialists.All.Should().Contain(
            specialist => specialist.ToolName == "site_analysis_site_geometry" &&
                          specialist.NodeKey == "site_geometry");
    }

    [Fact]
    public void Count_ShouldEqualTheDeclaredBranches()
    {
        SiteAnalysisSpecialists.Count.Should().Be(SiteAnalysisSpecialists.All.Count);
        SiteAnalysisSpecialists.All.Select(specialist => specialist.ToolName).Should().OnlyHaveUniqueItems();
        SiteAnalysisSpecialists.All.Select(specialist => specialist.NodeKey).Should().OnlyHaveUniqueItems();
    }
}
