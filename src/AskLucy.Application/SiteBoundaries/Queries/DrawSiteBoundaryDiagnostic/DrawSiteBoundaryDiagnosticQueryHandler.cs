using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.SiteBoundaries.Queries.DrawSiteBoundaryDiagnostic;

public sealed class DrawSiteBoundaryDiagnosticQueryHandler(
    ISatelliteImageProvider satelliteImageProvider, IBoundaryDrawDiagnosticService drawService)
    : IRequestHandler<DrawSiteBoundaryDiagnosticQuery, BoundaryDrawDiagnosticResult>
{
    public async Task<BoundaryDrawDiagnosticResult> Handle(DrawSiteBoundaryDiagnosticQuery request, CancellationToken cancellationToken)
    {
        var center = new GeoPoint(request.Latitude, request.Longitude);
        var image = await satelliteImageProvider.FetchAsync(center, request.RadiusMeters, cancellationToken);

        return image is null
            ? new BoundaryDrawDiagnosticResult(null, null, "No map image available — check the Google Maps API key is configured.")
            : await drawService.DrawAsync(image, request.SiteName, cancellationToken);
    }
}
