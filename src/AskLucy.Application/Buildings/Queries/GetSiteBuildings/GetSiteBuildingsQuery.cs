using AskLucy.Application.Buildings;
using MediatR;

namespace AskLucy.Application.Buildings.Queries.GetSiteBuildings;

/// <summary>
/// specs/052-solar-analysis contracts/building-footprints-endpoint.md — GET /api/v1/site-buildings.
/// The default radius (200 m) is resolved by the controller, from the same
/// <c>BuildingRetrievalOptions</c> the provider itself reads its count cap and cache TTL from
/// (Infrastructure), so this Application-layer query stays free of an Infrastructure reference
/// (constitution §2.I Dependency Rule) while still carrying one source of truth for the default.
/// </summary>
public sealed record GetSiteBuildingsQuery(double Latitude, double Longitude, int RadiusMetres)
    : IRequest<BuildingFootprintResult>;
