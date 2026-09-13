using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.Buildings.Queries.GetSiteBuildings;

/// <summary>
/// specs/052-solar-analysis — no business logic beyond delegating to
/// <see cref="IBuildingFootprintProvider"/>, which is Infrastructure-isolated (constitution §3).
/// </summary>
public sealed class GetSiteBuildingsQueryHandler(IBuildingFootprintProvider provider)
    : IRequestHandler<GetSiteBuildingsQuery, BuildingFootprintResult>
{
    public Task<BuildingFootprintResult> Handle(GetSiteBuildingsQuery request, CancellationToken cancellationToken)
    {
        var center = new GeoPoint(request.Latitude, request.Longitude);
        return provider.SearchAsync(center, request.RadiusMetres, cancellationToken);
    }
}
