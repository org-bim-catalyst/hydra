namespace AskLucy.Application.Viewer;

/// <summary>One marker the viewer should place for a POI layer.</summary>
public sealed record PoiMarkerData(string Id, string Name, double Latitude, double Longitude);

/// <summary>
/// specs/090-poi-discovery — the POI layers Lucy asked the viewer to draw, carried on
/// <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk.PoiLayer"/> exactly as
/// <see cref="ViewerContentCommand"/> is. The frontend owns layer state and rendering; this record
/// only says which category arrived, how it should look, and whether it joins or replaces the
/// layers already on the map.
/// </summary>
/// <param name="CategoryKey">Stable category key (e.g. <c>school</c>).</param>
/// <param name="DisplayName">Plural user-facing name for the legend.</param>
/// <param name="Color">Marker colour, distinct per category.</param>
/// <param name="MarkerShape">One of the existing POI overlay shapes.</param>
/// <param name="Mode"><c>replace</c> clears the other layers; <c>keep</c> draws beside them.</param>
/// <param name="Markers">The points to draw, already capped by the capability.</param>
public sealed record PoiLayerCommand(
    string CategoryKey,
    string DisplayName,
    string Color,
    string MarkerShape,
    string Mode,
    IReadOnlyList<PoiMarkerData> Markers);
