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
    IUserChatRepository userChatRepository,
    IMessageRepository messageRepository) : IConversationCapability
{
    public const string CapabilityKey = "find_points_of_interest";

    private const int DefaultRadiusMeters = 10_000;
    private const int MinRadiusMeters = 2_000;
    private const int MaxRadiusMeters = 25_000;

    public string Name => CapabilityKey;

    public string Description =>
        "Finds every place of one type — schools, hospitals, mosques, pharmacies, banks, parks — " +
        "in a named area and shows them as map markers. Also continues an outstanding clear-or-keep " +
        "question about layers already on the map.";

    public string WhenToUse =>
        "Use when the user asks to see, find or show a CATEGORY of place in an area — \"schools " +
        "in Muscat\", \"all hospitals in Oman\", \"mosques near here\" — especially a plural or " +
        "\"all the ...\" request. Never for one named place; that is resolve_location.";

    public string ArgumentHint =>
        "place type (e.g. schools) and area (e.g. Oman)";

    public string UsageGuidance =>
        "Pass the category and area as the user said them; omit the area only for \"here\"/\"this " +
        "area\", which use the place on screen. If the result has awaitingChoice=true, ask its " +
        "question and claim NOTHING was drawn — nothing was. On \"keep both\" call again with the " +
        "same category and mode=keep; on \"clear\"/\"replace\", mode=replace; the pending request " +
        "remembers the area. If awaitingArea=true, ask which area to search and claim nothing was " +
        "drawn. Report the count and area used. If count is 0, say plainly that none were found. " +
        "If the search failed, say so and never claim anything was drawn or cleared.";

    public string Label => "Map places by type";

    public string OfferDescription => "Show every school, hospital or mosque in an area on the map.";

    public string AcknowledgementTemplate => "Searching the map for them.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","properties":{"category":{"type":"string","minLength":1},"area":{"type":"string","minLength":1},"mode":{"type":"string","enum":["keep","replace"]}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"awaitingChoice":{"type":"boolean"},"awaitingArea":{"type":"boolean"},"question":{"type":"string"},"category":{"type":"string"},"displayName":{"type":"string"},"color":{"type":"string"},"markerShape":{"type":"string"},"mode":{"type":"string"},"count":{"type":"integer"},"areaName":{"type":"string"},"markers":{"type":"array"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Extended;

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
        var chatId = context.UserChatId ?? Guid.Empty;
        var categoryPhrase = ReadString(root, "category", "type", "placeType", "place_type", "poi", "poiType", "kind", "place");

        // Read once, and only if needed: the message is the fallback for a missing category and
        // the source of a keep/replace answer the decided arguments did not carry.
        IReadOnlyList<Domain.Chats.Message>? messages = null;
        async Task<string?> LastUserMessageAsync()
        {
            messages ??= await messageRepository.ListByChatIdAsync(chatId, cancellationToken) ?? [];
            return messages.LastOrDefault(m => m.Role == Domain.Chats.MessageRole.User)?.Content;
        }

        var pending = layerStateStore.GetPending(chatId);

        if (!categoryCatalog.TryResolve(categoryPhrase, out var category))
        {
            // An answer to either question carries no category of its own ("yes", "Oman"): the
            // pending request is what remembers it.
            var fromPending = pending is not null &&
                              categoryCatalog.TryResolve(pending.CategoryKey, out category);

            // Second chance from the message itself: the deciding model supplies only arguments,
            // and it has been observed to omit the one field this capability exists for. Reading
            // the user's own words is more reliable than failing a request whose category is
            // plainly right there in the sentence.
            if (!fromPending && !categoryCatalog.TryResolveFromText(await LastUserMessageAsync(), out category))
            {
                return AgentToolResult.Failure(
                    $"I can't tell which place type to map — I know these: {categoryCatalog.DescribeSupported()}.");
            }
        }

        var mode = ReadString(root, "mode");
        var requestedArea = ReadString(root, "area", "location", "city", "region", "country");

        // The example in the argument hint ("schools in Muscat") has been copied into `area` as if
        // it were a place; it is not one, and geocoding it used to search the stale active pin.
        if (requestedArea is not null && categoryCatalog.TryResolveFromText(requestedArea, out _))
        {
            requestedArea = null;
        }

        var answeringKeepOrReplace =
            pending?.Question == PoiPendingQuestion.KeepOrReplace &&
            string.Equals(pending.CategoryKey, category.Key, StringComparison.OrdinalIgnoreCase);

        // A mode-less request while a DIFFERENT category is on the map: ask before drawing. The
        // pending request remembers what was asked, so the answer's own invocation needs no area.
        // Re-searching the same category is not a conflict — it refreshes that one layer.
        var activeLayers = layerStateStore.GetLayers(chatId);
        var otherLayers = activeLayers
            .Where(l => !string.Equals(l.Key, category.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mode is null && otherLayers.Count > 0 && !answeringKeepOrReplace)
        {
            layerStateStore.SetPending(chatId, new PendingPoiRequest(
                category.Key, requestedArea, PoiPendingQuestion.KeepOrReplace, DateTime.UtcNow));

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

        // The user's answer, when the decided arguments did not carry it. Without this, a bare
        // "yes" re-asked the same question forever, because the decider never saw the Tier 2
        // guidance telling it to pass mode. "keep" is explicit; anything else means replace.
        if (mode is null && answeringKeepOrReplace)
        {
            var answer = await LastUserMessageAsync();
            mode = answer is not null && answer.Contains("keep", StringComparison.OrdinalIgnoreCase)
                ? "keep"
                : "replace";
        }

        var keepExisting = string.Equals(mode, "keep", StringComparison.OrdinalIgnoreCase);

        // The area: the decided argument, the pending request's own copy, or the map itself for
        // "here"/"near me". Never the stale active pin for an area the user did not name — that is
        // how "schools in Oman" once searched a layer called "schools in muscat".
        var areaName = requestedArea ?? (pending is not null &&
            string.Equals(pending.CategoryKey, category.Key, StringComparison.OrdinalIgnoreCase)
                ? pending.AreaName
                : null);

        GeoPoint? center = null;
        ViewportBounds? viewport = null;

        if (string.IsNullOrWhiteSpace(areaName) && MentionsHere(await LastUserMessageAsync()))
        {
            var chat = await userChatRepository.GetByIdAsync(chatId, cancellationToken);
            if (chat?.ActiveLocation is { } active)
            {
                areaName = active.LocationName;
                center = new GeoPoint(active.Latitude, active.Longitude);
            }
        }

        if (string.IsNullOrWhiteSpace(areaName))
        {
            layerStateStore.SetPending(chatId, new PendingPoiRequest(
                category.Key, null, PoiPendingQuestion.WhichArea, DateTime.UtcNow));

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                awaitingArea = true,
                question = $"Which area should I search for {category.DisplayName.ToLowerInvariant()}?",
                category = category.Key,
                displayName = category.DisplayName,
            }));
        }

        if (center is null)
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

        var radius = RadiusFor(viewport, center);
        var bounds = viewport is null
            ? null
            : new PoiBoundingBox(
                Math.Min(viewport.SouthwestLat, viewport.NortheastLat),
                Math.Min(viewport.SouthwestLng, viewport.NortheastLng),
                Math.Max(viewport.SouthwestLat, viewport.NortheastLat),
                Math.Max(viewport.SouthwestLng, viewport.NortheastLng));

        IReadOnlyList<PoiFeature> features;
        try
        {
            features = await poiQueryProvider.QueryAsync(
                new PoiQuery(center, radius, category, Bounds: bounds, AreaName: areaName), cancellationToken);
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

    private static bool MentionsHere(string? message) =>
        message is not null &&
        (message.Contains("here", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("this area", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("nearby", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("near me", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("around me", StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var element) &&
                element.ValueKind == JsonValueKind.String &&
                element.GetString() is { Length: > 0 } value &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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
