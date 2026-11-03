using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Locations;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Finds every place of one category in an area and asks the viewer to draw them as markers
/// (specs/090-poi-discovery).
///
/// <para>
/// <b>Why this exists as its own capability.</b> A request for a <i>category</i> of place —
/// "show me schools in Muscat" — is not a request for one named place. Routing it through
/// <see cref="ResolveLocationCapability"/> geocodes the phrase as a single point and then the
/// <c>locate_a_place</c> flow outlines a tiny site boundary around it, which is exactly the wrong
/// answer: the user asked for many places, and got one pin. Category searches therefore get their
/// own capability and their own routing words, and the location capability's own wording says to
/// leave these requests to it.
/// </para>
///
/// <para>
/// <b>Layer management (FR-008/FR-009).</b> When another category is already drawn, this
/// capability <i>does not search yet</i>. It returns a question and the active category names, so
/// Lucy asks "clear them or keep them alongside?" before touching the user's map. The user's
/// answer arrives as a second invocation carrying <c>mode</c> — and the pending request remembers
/// the category and area, so the search still runs only once.
/// </para>
/// </summary>
public sealed class FindPointsOfInterestCapability(
    ILocationResolutionService locationResolutionService,
    IPoiQueryProvider poiQueryProvider,
    PoiCategoryCatalog categoryCatalog,
    PoiLayerStateStore layerStateStore,
    IUserChatRepository userChatRepository) : IConversationCapability
{
    public const string CapabilityKey = "find_points_of_interest";

    private const int DefaultRadiusMeters = 10_000;
    private const int MinRadiusMeters = 2_000;
    private const int MaxRadiusMeters = 25_000;

    public string Name => CapabilityKey;

    public string Description =>
        "Finds every place of one type — schools, hospitals, mosques, pharmacies, banks, parks — " +
        "in a named area and shows them as map markers.";

    public string WhenToUse =>
        "Use when the user asks to see, find or show a CATEGORY of place in an area — \"show me " +
        "schools in Muscat\", \"where are the hospitals in Salalah\", \"find all mosques near " +
        "here\" — especially a plural or an \"all the ...\" request. Never for one named place, " +
        "site, building or address; that is resolve_location.";

    public string ArgumentHint =>
        "the category (e.g. schools, hospitals, mosques) and the area (e.g. Muscat); optional mode " +
        "keep or replace when answering a clear-or-keep question";

    public string UsageGuidance =>
        "Pass the category and the area exactly as the user said them; omit the area only for " +
        "\"here\"/\"this area\" requests, which use the place already on screen. " +
        "If the result has awaitingChoice=true, ask the user the question it carries, verbatim in " +
        "substance, and claim NOTHING was drawn — nothing was. When the user answers \"keep both\"/" +
        "\"keep them\", call again with the same category and mode=keep; when they answer " +
        "\"clear\"/\"replace\", call again with the same category and mode=replace; the pending " +
        "request remembers the area. Report the marker count and the area the search used. If " +
        "count is 0, say plainly that none were found in that area — never pretend markers exist.";

    public string Label => "Map places by type";

    public string OfferDescription => "Show every school, hospital or mosque in an area on the map.";

    public string AcknowledgementTemplate => "Searching the map for them.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","required":["category"],"properties":{"category":{"type":"string","minLength":1},"area":{"type":"string","minLength":1},"mode":{"type":"string","enum":["keep","replace"]}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"awaitingChoice":{"type":"boolean"},"question":{"type":"string"},"category":{"type":"string"},"displayName":{"type":"string"},"color":{"type":"string"},"markerShape":{"type":"string"},"mode":{"type":"string"},"count":{"type":"integer"},"areaName":{"type":"string"},"markers":{"type":"array"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Noticeable;

    public SubAgentArea Area => SubAgentArea.Location;

    /// <summary>
    /// Always available. A POI request can name its own area, and an unsupported category or a
    /// missing area is answered in words — hiding the capability would make those requests
    /// unreachable rather than better handled.
    /// </summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>Never offered unprompted — these are asked for, not suggested.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;
        var categoryPhrase = root.TryGetProperty("category", out var categoryEl) ? categoryEl.GetString() : null;

        if (!categoryCatalog.TryResolve(categoryPhrase, out var category))
        {
            return AgentToolResult.Failure(
                $"I can't map \"{categoryPhrase}\" — I know these place types: {categoryCatalog.DescribeSupported()}.");
        }

        var chatId = context.UserChatId ?? Guid.Empty;
        var mode = root.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
        var requestedArea = root.TryGetProperty("area", out var areaEl) ? areaEl.GetString() : null;

        // A mode-less request while a DIFFERENT category is on the map: ask before drawing. The
        // pending request remembers what was asked, so the answer's own invocation needs no area.
        // Re-searching the same category is not a conflict — it refreshes that one layer.
        var activeLayers = layerStateStore.GetLayers(chatId);
        var otherLayers = activeLayers
            .Where(l => !string.Equals(l.Key, category.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mode is null && otherLayers.Count > 0)
        {
            layerStateStore.SetPending(chatId, new PendingPoiRequest(category.Key, requestedArea, DateTime.UtcNow));

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                awaitingChoice = true,
                question =
                    $"You already have {JoinNames(otherLayers)} on the map. Should I clear those markers " +
                    $"and show {category.DisplayName.ToLowerInvariant()} instead, or keep them alongside " +
                    "the new results?",
                category = category.Key,
                displayName = category.DisplayName,
                activeCategories = otherLayers.Select(l => new { key = l.Key, displayName = l.DisplayName }),
            }));
        }

        // The answer's copy carries only the mode; category/area come from the pending request.
        var pending = layerStateStore.GetPending(chatId);
        if (pending is not null &&
            (mode is not null || string.IsNullOrWhiteSpace(requestedArea)) &&
            string.Equals(pending.CategoryKey, category.Key, StringComparison.OrdinalIgnoreCase))
        {
            requestedArea ??= pending.AreaName;
        }

        var keepExisting = string.Equals(mode, "keep", StringComparison.OrdinalIgnoreCase);

        // The area: the user's words, or the place already on screen for "here"/"this area".
        var areaName = requestedArea;
        GeoPoint? center = null;
        ViewportBounds? viewport = null;

        if (!string.IsNullOrWhiteSpace(areaName))
        {
            var outcome = await locationResolutionService.ResolveQueryAsync(chatId, areaName, cancellationToken);
            if (outcome.ConfirmedLocation is not { } resolved)
            {
                return AgentToolResult.Failure(
                    outcome.ConfirmationText ?? $"I couldn't resolve \"{areaName}\" to an area on the map.");
            }

            areaName = resolved.LocationName;
            center = new GeoPoint(resolved.Latitude, resolved.Longitude);
            viewport = resolved.Viewport;
        }
        else
        {
            var chat = await userChatRepository.GetByIdAsync(chatId, cancellationToken);
            if (chat?.ActiveLocation is not { } active)
            {
                return AgentToolResult.Failure(
                    "Tell me which area to search — for example \"schools in Muscat\".");
            }

            areaName = active.LocationName;
            center = new GeoPoint(active.Latitude, active.Longitude);
        }

        var radius = RadiusFor(viewport, center);

        IReadOnlyList<PoiFeature> features;
        try
        {
            features = await poiQueryProvider.QueryAsync(
                new PoiQuery(center, radius, category), cancellationToken);
        }
        catch (BoundaryProviderUnavailableException ex)
        {
            // Expected, ordinary failure: the free public Overpass endpoints saturate. Reported as a
            // failure so Lucy says the lookup failed — never as an empty result, which would claim
            // the area has no schools.
            return AgentToolResult.Failure(
                $"The map data service was unavailable while searching {areaName} ({ex.Message}).");
        }

        if (features.Count == 0)
        {
            if (!keepExisting)
            {
                // The user chose to replace; honour that even when the new search is empty.
                layerStateStore.Clear(chatId);
            }

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                category = category.Key,
                displayName = category.DisplayName,
                color = category.Color,
                markerShape = category.MarkerShape,
                mode = keepExisting ? "keep" : "replace",
                count = 0,
                areaName,
                markers = Array.Empty<object>(),
            }));
        }

        layerStateStore.ApplyLayer(chatId, category, keepExisting);

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            category = category.Key,
            displayName = category.DisplayName,
            color = category.Color,
            markerShape = category.MarkerShape,
            mode = keepExisting ? "keep" : "replace",
            count = features.Count,
            areaName,
            markers = features
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new { id = f.Id, name = f.Name, latitude = f.Latitude, longitude = f.Longitude }),
        }));
    }

    /// <summary>
    /// A search radius that covers the named place's own extent, clamped so a city name is not
    /// searched as the whole country and a district name is not searched as one street. Falls
    /// back to a city-sized default when the geocoder carried no viewport.
    /// </summary>
    private static int RadiusFor(ViewportBounds? viewport, GeoPoint center)
    {
        if (viewport is null)
        {
            return DefaultRadiusMeters;
        }

        var latMetres = Math.Abs(viewport.NortheastLat - viewport.SouthwestLat) * 111_320d;
        var lonMetres = Math.Abs(viewport.NortheastLng - viewport.SouthwestLng) *
                        111_320d * Math.Cos(center.Latitude * Math.PI / 180d);

        var halfExtent = Math.Max(latMetres, lonMetres) / 2d;
        return (int)Math.Clamp(halfExtent, MinRadiusMeters, MaxRadiusMeters);
    }

    private static string JoinNames(IReadOnlyList<PoiCategoryDefinition> layers) =>
        layers.Count == 1
            ? layers[0].DisplayName.ToLowerInvariant()
            : string.Join(", ", layers.Take(layers.Count - 1).Select(l => l.DisplayName.ToLowerInvariant())) +
              " and " + layers[^1].DisplayName.ToLowerInvariant();
}
