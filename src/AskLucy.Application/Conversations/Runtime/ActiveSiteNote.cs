using System.Globalization;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.SiteBoundaries;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/079 (research D8, FR-021) - the one line of context telling Lucy which outline is on screen
/// and how big it is. Built from the outline IN FORCE (<see cref="EffectiveSiteBoundary"/>), so after a
/// hand edit she reports the edited area and never describes Lucy's original as current. Without it
/// the model would answer "how big is the site?" from an earlier turn's numbers.
/// </summary>
public static class ActiveSiteNote
{
    /// <summary>The line, or null when the turn has no outline.</summary>
    public static string? Describe(TurnContext context) => Describe(context.ActiveBoundary);

    /// <summary>The line for an outline already resolved through <see cref="EffectiveSiteBoundary"/>, or null when there is none.</summary>
    public static string? Describe(Domain.Chats.ActiveSiteBoundary? boundary)
    {
        if (boundary is null)
        {
            return null;
        }

        var area = boundary.AreaSquareMeters.ToString("N0", CultureInfo.InvariantCulture);

        if (boundary.IsHandEdited)
        {
            return $"An active site boundary is shown for '{boundary.SiteName}': the outline the USER hand-edited, " +
                $"{area} m². This is the current outline. Any figure from before the edit is out of date. " +
                "If asked about its confidence or source, say the user shaped it themselves. " +
                "They can reset it to the outline you found.";
        }

        return $"An active site boundary is already shown for '{boundary.SiteName}' " +
            $"(confidence: {boundary.ConfidenceLevel}, source: {boundary.Source}, area: {area} m²).";
    }
}
