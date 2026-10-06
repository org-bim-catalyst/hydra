using AskLucy.Application.Locations;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Chats.Queries.GetChatById;

/// <summary>
/// specs/025-chat-configuration-settings, contracts/chat-detail-api.md — a single chat's
/// current provider/model selection, previously persisted but never queryable. Null
/// <see cref="ProviderId"/>/<see cref="ModelId"/> means the conversation has never had a
/// model selection saved yet (e.g. before its first message).
/// <para>
/// <see cref="ActiveLocation"/>/<see cref="ActiveBoundary"/> — the site the conversation last
/// confirmed and outlined. Both were persisted since specs/037/042 but only ever reached the
/// client through a live reply's stream, so reopening the chat (or a page reload mid-turn) left
/// the viewer without the site the conversation — and Lucy's own context — still considered
/// current. Null when the conversation has not confirmed one.
/// </para>
/// </summary>
public sealed record ChatDetailDto(
    Guid Id,
    string Title,
    Guid? ProviderId,
    Guid? ModelId,
    ChatActiveLocationDto? ActiveLocation,
    ChatActiveBoundaryDto? ActiveBoundary)
{
    /// <param name="effectiveBoundary">The outline in force (specs/079): the chat's own found outline, or the user's hand-edited one when the chat links to it.</param>
    public static ChatDetailDto FromEntity(UserChat chat, ActiveSiteBoundary? effectiveBoundary) => new(
        chat.Id,
        chat.Title,
        chat.ProviderId,
        chat.ModelId,
        chat.ActiveLocation is { } location ? ChatActiveLocationDto.FromEntity(location) : null,
        effectiveBoundary is { } boundary ? ChatActiveBoundaryDto.FromEntity(boundary) : null);
}

/// <summary>
/// <see cref="ConfidenceLevel"/> is lower-case, as the live <c>__LOCATION__</c> event carries it.
/// </summary>
public sealed record ChatActiveLocationDto(
    double Latitude, double Longitude, string LocationName, double Confidence, string ConfidenceLevel, string ConfidenceReason)
{
    public static ChatActiveLocationDto FromEntity(ActiveSiteLocation location) => new(
        location.Latitude,
        location.Longitude,
        location.LocationName,
        location.Confidence,
        LocationConfidence.Classify(location.LocationType).ToString().ToLowerInvariant(),
        LocationConfidence.Explain(location.LocationType));
}

/// <summary>
/// The same shape as the live <c>__SITE_BOUNDARY__</c> event, so the client applies either one
/// the same way — including <see cref="ConfidenceLevel"/> as the lower-case string that event
/// carries rather than the enum's API-wide PascalCase serialisation. Alternative candidate names
/// are not persisted with the boundary, so a restored one has none.
/// </summary>
public sealed record ChatActiveBoundaryDto(
    string SiteName,
    ChatGeoPointDto Centroid,
    IReadOnlyList<ChatGeoPointDto> Polygon,
    double AreaSquareMeters,
    double Confidence,
    string ConfidenceLevel,
    string Source,
    string SourceDetail)
{
    /// <summary>specs/077 — outlines of included buildings standing apart from <see cref="Polygon"/>.</summary>
    public IReadOnlyList<IReadOnlyList<ChatGeoPointDto>> AdditionalPolygons { get; init; } = [];

    /// <summary>specs/081 — each ring's voids by ring index (0 is <see cref="Polygon"/>); empty when there are none.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<ChatGeoPointDto>>> Voids { get; init; } = [];

    /// <summary>specs/079 — the token a client sends back as <c>expectedRevision</c> when saving an edit.</summary>
    public string Revision { get; init; } = string.Empty;

    /// <summary>specs/079 — true when the outline is the user's hand-edited one.</summary>
    public bool IsHandEdited { get; init; }

    public static ChatActiveBoundaryDto FromEntity(ActiveSiteBoundary boundary) => new(
        boundary.SiteName,
        new ChatGeoPointDto(boundary.CentroidLatitude, boundary.CentroidLongitude),
        [.. boundary.Polygon.Select(p => new ChatGeoPointDto(p.Latitude, p.Longitude))],
        boundary.AreaSquareMeters,
        boundary.Confidence,
        boundary.ConfidenceLevel.ToString().ToLowerInvariant(),
        boundary.Source.ToString(),
        boundary.SourceDetail)
    {
        AdditionalPolygons = [.. boundary.AdditionalPolygons.Select(r => (IReadOnlyList<ChatGeoPointDto>)[.. r.Select(p => new ChatGeoPointDto(p.Latitude, p.Longitude))])],
        Voids = [.. boundary.Voids.Select(ringVoids => (IReadOnlyList<IReadOnlyList<ChatGeoPointDto>>)[
            .. ringVoids.Select(v => (IReadOnlyList<ChatGeoPointDto>)[.. v.Select(p => new ChatGeoPointDto(p.Latitude, p.Longitude))])])],
        Revision = boundary.Revision.ToString(),
        IsHandEdited = boundary.IsHandEdited,
    };
}

public sealed record ChatGeoPointDto(double Latitude, double Longitude);
