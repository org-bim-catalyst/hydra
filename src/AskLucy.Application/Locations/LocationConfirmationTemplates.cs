namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution — deterministic, template-based sentences for every
/// non-NoIntent <see cref="LocationResolutionOutcomeType"/>. Nominatim's display_name is
/// embedded as data into a fixed template sentence; it is never re-fed to any LLM call
/// (constitution §8 prompt injection).
/// </summary>
public static class LocationConfirmationTemplates
{
    public static string Confirmed(string locationName) =>
        $"I've located {locationName} and centred the viewer on it.";

    public const string Ambiguous =
        "That place name matches several different locations — could you be more specific?";

    public const string NotFound =
        "I couldn't find a place matching that name — please try a more specific name.";

    public const string Unavailable =
        "I couldn't look that up right now — please try again in a moment.";

    public const string BackReferenceNoActive =
        "I don't have an active location yet — please name the place you'd like to view.";
}
