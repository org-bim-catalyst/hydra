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
    public static ChatDetailDto FromEntity(UserChat chat) => new(
        chat.Id,
        chat.Title,
        chat.ProviderId,
        chat.ModelId,
        chat.ActiveLocation is { } location ? ChatActiveLocationDto.FromEntity(location) : null,
        chat.ActiveBoundary is { } boundary ? ChatActiveBoundaryDto.FromEntity(boundary) : null);
}

/// <summary>
/// <see cref="ConfidenceLevel"/> is lower-case, as the live <c>__LOCATION__</c> event carries it.
/// </summary>
public sealed record ChatActiveLocationDto(
    double Latitude, double Longitude, string LocationName, double Confidence, string ConfidenceLevel)
{
    public static ChatActiveLocationDto FromEntity(ActiveSiteLocation location) => new(
        location.Latitude,
        location.Longitude,
        location.LocationName,
        location.Confidence,
        LocationConfidence.Classify(location.LocationType).ToString().ToLowerInvariant());
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
    public static ChatActiveBoundaryDto FromEntity(ActiveSiteBoundary boundary) => new(
        boundary.SiteName,
        new ChatGeoPointDto(boundary.CentroidLatitude, boundary.CentroidLongitude),
        [.. boundary.Polygon.Select(p => new ChatGeoPointDto(p.Latitude, p.Longitude))],
        boundary.AreaSquareMeters,
        boundary.Confidence,
        boundary.ConfidenceLevel.ToString().ToLowerInvariant(),
        boundary.Source.ToString(),
        boundary.SourceDetail);
}

public sealed record ChatGeoPointDto(double Latitude, double Longitude);
