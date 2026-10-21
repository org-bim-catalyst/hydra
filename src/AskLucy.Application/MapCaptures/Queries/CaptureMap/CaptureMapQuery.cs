using MediatR;

namespace AskLucy.Application.MapCaptures.Queries.CaptureMap;

/// <summary>
/// specs/083-viewer-map-capture T015. Mode and overlays stay strings so the controller can keep
/// raw wire values; <see cref="CaptureMapQueryValidator"/> and the handler share the explicit
/// parser in <see cref="MapCaptureWireFormat"/>.
/// </summary>
public sealed record CaptureMapQuery(
    string? Location,
    double? Latitude,
    double? Longitude,
    int? ViewSizeMetres,
    string? Mode,
    string? Overlays,
    string CallerName) : IRequest<MapImageResult>;
