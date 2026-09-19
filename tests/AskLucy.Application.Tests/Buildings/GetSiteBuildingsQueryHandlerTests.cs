using AskLucy.Application.Buildings;
using AskLucy.Application.Buildings.Queries.GetSiteBuildings;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Buildings;

/// <summary>
/// specs/052-solar-analysis T035 — the handler delegates to <see cref="IBuildingFootprintProvider"/>
/// with no business logic of its own; a faked provider, no network (constitution §10).
/// </summary>
public sealed class GetSiteBuildingsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldDelegateToProvider_WithTheRequestedCenterAndRadius()
    {
        var provider = Substitute.For<IBuildingFootprintProvider>();
        var expected = new BuildingFootprintResult([], false, 0, 200);
        provider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetSiteBuildingsQueryHandler(provider);
        var result = await handler.Handle(new GetSiteBuildingsQuery(25.2, 55.3, 200), CancellationToken.None);

        result.Should().Be(expected);
        await provider.Received(1).SearchAsync(
            Arg.Is<GeoPoint>(p => p != null && p.Latitude == 25.2 && p.Longitude == 55.3), 200, Arg.Any<CancellationToken>());
    }
}
