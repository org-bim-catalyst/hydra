using AskLucy.Application.Buildings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.SiteAnalysis;

/// <summary>
/// specs/075 — the footprint chain is three keyed providers behind a keyed composite behind a
/// decorator, and the height source needs an internal decoder. None of that wiring is visible to
/// the unit tests, which construct each piece by hand; this resolves it from the real host.
/// </summary>
public sealed class BuildingProviderRegistrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public void BuildingProviders_ShouldResolveFromTheHost_WithTheHeightDecoratorOutermost()
    {
        using var scope = factory.Services.CreateScope();

        var footprints = scope.ServiceProvider.GetRequiredService<IBuildingFootprintProvider>();
        scope.ServiceProvider.GetRequiredService<IBuildingHeightSource>().Should().NotBeNull();

        footprints.GetType().Name.Should().Be("HeightEnrichingBuildingFootprintProvider");
    }
}
