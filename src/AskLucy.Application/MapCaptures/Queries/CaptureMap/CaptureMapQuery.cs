using MediatR;

namespace AskLucy.Application.MapCaptures.Queries.CaptureMap;

/// <summary>
/// specs/083-viewer-map-capture T015. Mode and overlays stay strings so the controller can keep
/// raw wire values; <see cref="CaptureMapQueryValidator"/> and the handler share the explicit
/// parser in <see cref="MapCaptureWireFormat"/>.
/// <para>
/// The source, the crop and the zoom are optional: a request that names none takes the defaults
/// an administrator configured on the admin AI Tools page, and the ones the catalogue declares
/// when nothing was ever configured.
/// </para>
/// </summary>
public sealed record CaptureMapQuery(
    string? Location,
    double? Latitude,
    double? Longitude,
    int? ViewSizeMetres,
    string? Mode,
    string? Overlays,
    string CallerName,
    string? Source = null,
    int? CropPixels = null,
    int? Zoom = null) : IRequest<MapImageResult>;
