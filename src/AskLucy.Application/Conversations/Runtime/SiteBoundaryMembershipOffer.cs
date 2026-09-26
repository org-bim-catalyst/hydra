using System.Text.Json;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Conversations;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/077 — the question asked after a site is outlined with related buildings: which of them
/// does the site include? Built here from the buildings actually found rather than composed by the
/// offer model, because every row must carry the exact member ids it stands for — a model
/// paraphrasing "the mall and the hotel" would be a guess at an argument list, and the answer
/// decides what every later analysis of the site covers.
/// <para>
/// Each option adds a group to the one before: the site alone, then its connected buildings, then
/// same-named buildings across the street, then stations. Groups that were not found are not
/// offered, and the letters follow what is offered. A last free-text row lets the user name any
/// other combination, and the standard decline row keeps the outline as drawn.
/// </para>
/// </summary>
public static class SiteBoundaryMembershipOffer
{
    private const int MaxNamesListed = 3;

    public static SuggestedActionOffer? Build(ConfirmedSiteBoundaryData boundary)
    {
        var members = boundary.Members;
        if (members.Count == 0)
        {
            return null;
        }

        var connected = members.Where(m => m.Kind == SiteBoundaryMemberKind.Building && m.Relation == SiteBoundaryMemberRelation.Connected).ToList();
        var nearby = members.Where(m => m.Kind == SiteBoundaryMemberKind.Building && m.Relation == SiteBoundaryMemberRelation.Nearby).ToList();
        var stations = members.Where(m => m.Kind == SiteBoundaryMemberKind.TransportStation).ToList();

        var site = boundary.SiteName;
        var options = new List<(string Label, string Description, IReadOnlyList<SiteBoundaryMember> Included)>
        {
            ($"{site} only", "Just the site itself, without any of the buildings below.", []),
        };

        var included = new List<SiteBoundaryMember>();
        if (connected.Count > 0)
        {
            included = [.. included, .. connected];
            options.Add(($"{site} with its connected buildings", connected.Count == 1
                ? $"Adds {NamesOf(connected)}, which shares its podium."
                : $"Adds {NamesOf(connected)} — they share its podium.", included));
        }

        if (nearby.Count > 0)
        {
            included = [.. included, .. nearby];
            options.Add((connected.Count > 0 ? "Also the buildings across the street" : $"{site} with its nearby buildings",
                nearby.Count == 1
                    ? $"Adds {NamesOf(nearby)}, which carries its name but stands apart."
                    : $"Adds {NamesOf(nearby)}, which carry its name but stand apart.", included));
        }

        if (stations.Count > 0)
        {
            included = [.. included, .. stations];
            options.Add(("Everything, including the station", $"Adds {NamesOf(stations)} as well.", included));
        }

        var shown = members.Where(m => m.Included).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var rows = new List<SuggestedAction>();
        foreach (var (option, index) in options.Select((o, i) => (o, i)))
        {
            var ids = option.Included.Select(m => m.Id).ToList();
            var isShown = ids.Count == shown.Count && ids.All(shown.Contains);
            rows.Add(new SuggestedAction(
                SuggestedActionKind.Capability,
                SetSiteBoundaryMembersCapability.CapabilityKey,
                Text: null,
                Fit($"{Letter(index)}. {option.Label}{(isShown ? " (shown now)" : string.Empty)}", SuggestedAction.MaxLabelLength),
                Fit(option.Description, SuggestedAction.MaxDescriptionLength),
                JsonSerializer.Serialize(new { memberIds = ids })));
        }

        rows.Add(new SuggestedAction(
            SuggestedActionKind.FollowUp,
            Key: null,
            Fit($"Ask which of these buildings the user wants in the {site} site: {string.Join(", ", members.Select(m => m.Name))}. " +
                $"Then call {SetSiteBoundaryMembersCapability.CapabilityKey} with exactly those.", SuggestedAction.MaxTextLength),
            $"{Letter(options.Count)}. Other — I'll name the buildings",
            "Pick any combination yourself.",
            ArgumentsJson: null));
        rows.Add(SuggestedAction.Decline("Keep the outline as it is"));

        return new SuggestedActionOffer(Fit($"Which buildings should the {site} site include?", 200), rows);
    }

    private static char Letter(int index) => (char)('A' + index);

    private static string NamesOf(IReadOnlyList<SiteBoundaryMember> members)
    {
        var names = members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var listed = names.Take(MaxNamesListed).ToList();
        var more = names.Count - listed.Count;
        var list = listed.Count == 1
            ? listed[0]
            : string.Join(", ", listed.Take(listed.Count - 1)) + " and " + listed[^1];
        return more > 0 ? $"{list} (+{more} more)" : list;
    }

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
