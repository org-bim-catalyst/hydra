namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// One specialist in the site-analysis fan-out: the <c>NativeTool</c> name it is invoked by and the
/// stable node key it occupies in the published workflow graph.
/// </summary>
public sealed record SiteAnalysisSpecialist(string ToolName, string NodeKey);

/// <summary>
/// specs/088-environmental-context-specialist — the single declaration of the site-analysis fan-out,
/// and the single source of the analysis's expected-result count.
///
/// <para>
/// Two readers depend on this list and they live in different assemblies: the capability that
/// creates the <c>SiteAnalysis</c> record (Application) and the provisioner that publishes the
/// fan-out workflow (Infrastructure). Application cannot reference Infrastructure, so the list
/// lives here: the provisioner projects it into workflow branches, and the capability uses
/// <see cref="Count"/> as the expected-result count.
/// </para>
///
/// <para>
/// Those two values must never diverge. If the count is lower than the number of branches, the
/// relay settles the analysis as soon as the first specialist reports and every later report is
/// rejected as arriving at a finished analysis — the finding is silently lost. Deriving both from
/// this list is what makes "add a specialist = one entry here plus one <c>IAgentTool</c>" (FR-031)
/// actually true.
/// </para>
/// </summary>
public static class SiteAnalysisSpecialists
{
    /// <summary>Every specialist dispatched for one analysis, in graph order.</summary>
    public static readonly IReadOnlyList<SiteAnalysisSpecialist> All =
    [
        new("site_analysis_schematic_image", "schematic_image"),
        new("site_analysis_environmental_context", "environmental_context"),
    ];

    /// <summary>The number of findings an analysis expects — the fan-out branch count.</summary>
    public static int Count => All.Count;
}
