using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/077 — chooses which of the outlined site's related buildings the highlight includes:
/// BurJuman's mall alone, or with its office tower and hotel, or with the tower across the street
/// too. The buildings were found when the site was resolved and are kept on the chat's active
/// boundary, so this is a redraw — no network call, which is why it is
/// <see cref="CapabilityDuration.Brief"/>.
/// <para>
/// Reached from the rows <see cref="Runtime.SiteBoundaryMembershipOffer"/> puts after a resolution
/// (each row carries its own <c>memberIds</c>), or from the user naming buildings in their own
/// words, which the model passes on as <c>memberNames</c>.
/// </para>
/// </summary>
public sealed class SetSiteBoundaryMembersCapability(
    IUserChatRepository userChatRepository, SiteBoundaryMembershipService membershipService) : IConversationCapability
{
    public const string CapabilityKey = "set_site_boundary_members";

    private static readonly string[] StationWords = ["station", "metro", "subway", "railway"];

    public string Name => CapabilityKey;

    public string Description =>
        "Chooses which buildings of the same development the outlined site includes, and redraws the outline.";

    public string WhenToUse =>
        "Use when a site is outlined and the user says which of its related buildings (towers, " +
        "hotels, stations named after it) the outline should include or leave out.";

    public string ArgumentHint =>
        "memberNames: the buildings to include, as the user named them; an empty list means the site alone";

    public string UsageGuidance =>
        "Pass every building the outline should include — the choice replaces the previous one, it " +
        "does not add to it. Then say which buildings the outline now covers and its new area. " +
        "excludedBuildings are simply not in the outline; call one removed only if the outline " +
        "included it before this choice.";

    public string Label => "Choose the site buildings";

    public string OfferDescription => "Include or leave out buildings of the same development.";

    public string AcknowledgementTemplate => "Now redrawing the site outline.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson =>
        """{"type":"object","properties":{"memberIds":{"type":"array","items":{"type":"string"}},"memberNames":{"type":"array","items":{"type":"string"}}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"siteName":{"type":"string"},"areaSquareMeters":{"type":"number"},"includedBuildings":{"type":"array"},"excludedBuildings":{"type":"array"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Location;

    /// <summary>Only meaningful once an outline with related buildings is on screen.</summary>
    public bool IsAvailable(TurnContext context) => context.ActiveBoundary is { Members.Count: > 0 };

    /// <summary>Never offered through the generic offer: its rows are built for the site at hand by <see cref="Runtime.SiteBoundaryMembershipOffer"/>.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var chat = context.UserChatId is { } chatId ? await userChatRepository.GetByIdAsync(chatId, cancellationToken) : null;
        if (chat?.ActiveBoundary is not { Members.Count: > 0 } active)
        {
            return AgentToolResult.Failure("No outlined site with related buildings is on screen to choose from.");
        }

        var root = input.RootElement;
        HashSet<string> chosen;
        if (root.TryGetProperty("memberIds", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
        {
            chosen = [.. idsElement.EnumerateArray().Select(e => e.GetString()).OfType<string>()];
            var unknown = chosen.Where(id => active.Members.All(m => m.Id != id)).ToList();
            if (unknown.Count > 0)
            {
                return AgentToolResult.Failure($"These buildings are not related to {active.SiteName}: {string.Join(", ", unknown)}.");
            }
        }
        else if (root.TryGetProperty("memberNames", out var namesElement) && namesElement.ValueKind == JsonValueKind.Array)
        {
            chosen = [];
            var unmatched = new List<string>();
            foreach (var name in namesElement.EnumerateArray().Select(e => e.GetString()).OfType<string>())
            {
                var match = MatchByName(active.Members, name);
                if (match is null)
                {
                    unmatched.Add(name);
                }
                else
                {
                    chosen.Add(match.Id);
                }
            }

            if (unmatched.Count > 0)
            {
                return AgentToolResult.Failure(
                    $"No building related to {active.SiteName} is called {string.Join(" or ", unmatched.Select(n => $"\"{n}\""))}. " +
                    $"The related buildings are: {string.Join(", ", active.Members.Select(m => m.Name))}.");
            }
        }
        else
        {
            return AgentToolResult.Failure("Say which buildings to include (memberIds or memberNames); an empty list means the site alone.");
        }

        var members = active.Members.Select(m => m with { Included = chosen.Contains(m.Id) }).ToList();
        var redrawn = membershipService.Compose(ToConfirmed(active), members);
        return AgentToolResult.Success(SiteBoundaryPayload.Write(redrawn));
    }

    /// <summary>
    /// The member the user meant: an exact name first, then one whose name contains what they
    /// said ("the office tower"), then a one-letter-off match for a mistyped name. Ambiguous
    /// containment — "tower" when there are two towers — matches nothing, so the model asks.
    /// </summary>
    private static SiteBoundaryMember? MatchByName(IReadOnlyList<SiteBoundaryMember> members, string name)
    {
        var exact = members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // A station is mapped under the site's own name ("BurJuman"), so "the metro station" can
        // only be matched on what it is.
        var stations = members.Where(m => m.Kind == SiteBoundaryMemberKind.TransportStation).ToList();
        if (stations.Count == 1 && StationWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return stations[0];
        }

        var cores = SiteNameMatcher.CoresOf([name]);
        if (cores.Count == 0)
        {
            return null;
        }

        var matches = members.Where(m => SiteNameMatcher.Matches([m.Name], cores)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static ConfirmedSiteBoundaryData ToConfirmed(ActiveSiteBoundary active) =>
        new(active.SiteName, active.CentroidLatitude, active.CentroidLongitude, active.Polygon, active.AreaSquareMeters,
            active.Confidence, active.ConfidenceLevel, active.Source, active.SourceDetail, AlternativeCandidateNames: [])
        {
            CorePolygon = active.CorePolygon,
            AdditionalPolygons = active.AdditionalPolygons,
            Members = active.Members,
        };
}
