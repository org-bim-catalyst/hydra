using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.SiteBoundaries.Queries.SegmentSiteBoundaryDiagnostic;

public sealed class SegmentSiteBoundaryDiagnosticQueryHandler(
    ISatelliteImageProvider satelliteImageProvider, IBoundarySegmentationDiagnosticService segmentationService)
    : IRequestHandler<SegmentSiteBoundaryDiagnosticQuery, BoundaryDrawDiagnosticResult>
{
    public async Task<BoundaryDrawDiagnosticResult> Handle(SegmentSiteBoundaryDiagnosticQuery request, CancellationToken cancellationToken)
    {
        var center = new GeoPoint(request.Latitude, request.Longitude);
        var image = await satelliteImageProvider.FetchAsync(center, request.RadiusMeters, cancellationToken);

        return image is null
            ? new BoundaryDrawDiagnosticResult(null, null, "No map image available — check the Google Maps API key is configured.")
            : await segmentationService.SegmentAsync(image, request.SiteName, cancellationToken);
    }
}
