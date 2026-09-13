using FluentValidation;

namespace AskLucy.Application.Buildings.Queries.GetSiteBuildings;

/// <summary>contracts/building-footprints-endpoint.md — 50…1000 m; a violation returns RFC 7807
/// Problem Details via <c>ValidationException</c> -&gt; <c>ProblemDetailsMiddleware</c> (§6).</summary>
public sealed class GetSiteBuildingsQueryValidator : AbstractValidator<GetSiteBuildingsQuery>
{
    public GetSiteBuildingsQueryValidator()
    {
        RuleFor(q => q.Latitude).InclusiveBetween(-90, 90);
        RuleFor(q => q.Longitude).InclusiveBetween(-180, 180);
        RuleFor(q => q.RadiusMetres).InclusiveBetween(50, 1000);
    }
}
