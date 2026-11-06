using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial;

/// <summary>
/// The outcome of asking where an operation should run. Exactly one of <see cref="Area"/>,
/// <see cref="ClarificationQuestion"/> or <see cref="FailureReason"/> is set.
/// </summary>
public sealed record StudyAreaResolution(
    ResolvedStudyArea? Area = null,
    string? ClarificationQuestion = null,
    string? FailureReason = null)
{
    public static StudyAreaResolution Resolved(ResolvedStudyArea area) => new(Area: area);

    public static StudyAreaResolution Ask(string question) => new(ClarificationQuestion: question);

    public static StudyAreaResolution Failed(string reason) => new(FailureReason: reason);
}

/// <summary>
/// Resolves where a spatial operation runs (FR-015/FR-016). Resolution order, per data-model.md:
/// explicit user statement → the outline already drawn → the current map view → ask. An explicit
/// place always wins; map context never silently overrides the user's own words.
/// </summary>
public sealed class StudyAreaResolver(
    ILocationResolutionService locationResolutionService,
    IUserChatRepository userChatRepository,
    EffectiveSiteBoundary effectiveSiteBoundary)
{
    private const int DefaultRadiusMeters = 10_000;
    private const int MinRadiusMeters = 2_000;
    private const int MaxRadiusMeters = 25_000;

    public async Task<StudyAreaResolution> ResolveAsync(
        Guid chatId,
        StudyAreaReference? requested,
        MapViewportContext? mapContext,
        CancellationToken cancellationToken)
    {
        UserChat? chat = null;
        async Task<UserChat?> ChatAsync()
        {
            chat ??= await userChatRepository.GetByIdAsync(chatId, cancellationToken);
            return chat;
        }

        if (requested is not null)
        {
            switch (requested.Kind)
            {
                case StudyAreaKind.NamedArea when !string.IsNullOrWhiteSpace(requested.NamedArea):
                {
                    var outcome = await locationResolutionService.ResolveQueryAsync(
                        chatId, requested.NamedArea, cancellationToken);

                    if (outcome?.ConfirmedLocation is not { } resolved)
                    {
                        // Only a genuinely ambiguous place is a question. A place that cannot be
                        // found (or whose provider is unavailable) is a failure the user must see,
                        // never a follow-up that implies a different answer would help.
                        if (outcome?.Type == LocationResolutionOutcomeType.Ambiguous &&
                            outcome.ConfirmationText is { Length: > 0 } question)
                        {
                            return StudyAreaResolution.Ask(question);
                        }

                        return StudyAreaResolution.Failed(
                            outcome?.ConfirmationText is { Length: > 0 } explanation
                                ? explanation
                                : $"I couldn't resolve \"{requested.NamedArea}\" to an area on the map.");
                    }

                    return StudyAreaResolution.Resolved(FromLocation(resolved));
                }

                case StudyAreaKind.ExplicitGeometry when requested.Polygon is { Count: >= 3 } polygon:
                    return StudyAreaResolution.Resolved(FromPolygon("the selected area", polygon));

                case StudyAreaKind.ActiveBoundary:
                {
                    var chatForBoundary = await ChatAsync();
                    var boundary = chatForBoundary is null
                        ? null
                        : await effectiveSiteBoundary.ResolveAsync(chatForBoundary, cancellationToken);

                    return boundary is null
                        ? StudyAreaResolution.Ask("Which area should I use? I don't have a site outline on the map right now.")
                        : StudyAreaResolution.Resolved(FromBoundary(boundary));
                }

                case StudyAreaKind.MapViewport:
                    return mapContext is null
                        ? StudyAreaResolution.Ask("Which area should I use? The map view isn't available right now.")
                        : StudyAreaResolution.Resolved(FromMapContext(mapContext));
            }
        }

        // No explicit area: the outline already drawn is the most specific context, then the map
        // view, then the confirmed active place.
        var activeBoundaryChat = await ChatAsync();
        var activeBoundary = activeBoundaryChat is null
            ? null
            : await effectiveSiteBoundary.ResolveAsync(activeBoundaryChat, cancellationToken);

        if (activeBoundary is not null)
        {
            return StudyAreaResolution.Resolved(FromBoundary(activeBoundary));
        }

        if (mapContext is not null)
        {
            return StudyAreaResolution.Resolved(FromMapContext(mapContext));
        }

        if ((await ChatAsync())?.ActiveLocation is { } activeLocation)
        {
            return StudyAreaResolution.Resolved(new ResolvedStudyArea(
                activeLocation.LocationName,
                new GeoPoint(activeLocation.Latitude, activeLocation.Longitude),
                DefaultRadiusMeters));
        }

        return StudyAreaResolution.Ask("Which area should I use?");
    }

    private static ResolvedStudyArea FromLocation(ConfirmedLocationData location)
    {
        var center = new GeoPoint(location.Latitude, location.Longitude);
        var radius = location.Viewport is null
            ? DefaultRadiusMeters
            : RadiusFor(center, location.Viewport);

        return new ResolvedStudyArea(location.LocationName, center, radius, Viewport: location.Viewport);
    }

    private static ResolvedStudyArea FromMapContext(MapViewportContext context)
    {
        var center = new GeoPoint(context.CenterLatitude, context.CenterLongitude);

        // Deliberately no Viewport on the result: a camera can be zoomed out to the whole world,
        // and passing that as a provider bounding box would turn "here" into a global query. The
        // radius, derived from the view and clamped, is the bounded search area.
        return new ResolvedStudyArea("the current map view", center, RadiusFor(center, ViewportOf(context)));
    }

    private static ViewportBounds ViewportOf(MapViewportContext context) =>
        new(
            context.NorthEastLatitude,
            context.NorthEastLongitude,
            context.SouthWestLatitude,
            context.SouthWestLongitude);

    private static ResolvedStudyArea FromBoundary(ActiveSiteBoundary boundary)
    {
        var center = new GeoPoint(boundary.CentroidLatitude, boundary.CentroidLongitude);
        var radius = RadiusFor(center, BoundaryBounds(boundary.Polygon));

        return new ResolvedStudyArea(
            boundary.SiteName,
            center,
            radius,
            boundary.Polygon,
            Viewport: BoundaryBounds(boundary.Polygon));
    }

    private static ResolvedStudyArea FromPolygon(string displayName, IReadOnlyList<GeoPoint> polygon)
    {
        var center = new GeoPoint(
            polygon.Average(point => point.Latitude),
            polygon.Average(point => point.Longitude));

        var viewport = BoundaryBounds(polygon);
        return new ResolvedStudyArea(displayName, center, RadiusFor(center, viewport), polygon, viewport);
    }

    private static ViewportBounds BoundaryBounds(IReadOnlyList<GeoPoint> polygon) =>
        new(
            polygon.Max(point => point.Latitude),
            polygon.Max(point => point.Longitude),
            polygon.Min(point => point.Latitude),
            polygon.Min(point => point.Longitude));

    /// <summary>
    /// A search radius that covers the area's own extent, clamped so a city name is not searched
    /// as the whole country and a district name is not searched as one street. Mirrors spec 090's
    /// established behaviour.
    /// </summary>
    private static int RadiusFor(GeoPoint center, ViewportBounds viewport)
    {
        var latMetres = Math.Abs(viewport.NortheastLat - viewport.SouthwestLat) * 111_320d;
        var lonMetres = Math.Abs(viewport.NortheastLng - viewport.SouthwestLng) *
                        111_320d * Math.Cos(center.Latitude * Math.PI / 180d);

        var halfExtent = Math.Max(latMetres, lonMetres) / 2d;
        return (int)Math.Clamp(halfExtent, MinRadiusMeters, MaxRadiusMeters);
    }
}
