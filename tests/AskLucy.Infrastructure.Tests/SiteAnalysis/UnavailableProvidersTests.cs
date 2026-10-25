using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Infrastructure.SiteAnalysis.Providers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.SiteAnalysis;

/// <summary>
/// specs/088-environmental-context-specialist FR-017/FR-018 — the empty implementations that make the
/// environmental seams constructible. They must report *unavailable* with a reason and must never
/// throw: unavailability is an outcome, not a fault (FR-035), and a throw would be captured as a
/// specialist failure and lose the finding.
/// </summary>
public sealed class UnavailableProvidersTests
{
    [Fact]
    public async Task FloodProvider_ShouldAlwaysReportUnavailable_WithAReason()
    {
        var outcome = await new UnavailableFloodDataProvider().ResolveAsync(25.09, 55.20, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(DataResolutionOutcomeType.Unavailable);
        outcome.Value.Should().BeNull();
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ClimateProvider_ShouldAlwaysReportUnavailable_WithAReason()
    {
        var outcome = await new UnavailableClimateDataProvider().ResolveAsync(25.09, 55.20, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(DataResolutionOutcomeType.Unavailable);
        outcome.Value.Should().BeNull();
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(-90.0, -180.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 180.0)]
    public async Task Seams_ShouldReportTheSameUnavailability_ForEveryPoint(double latitude, double longitude)
    {
        var flood = await new UnavailableFloodDataProvider().ResolveAsync(latitude, longitude, TestContext.Current.CancellationToken);
        var climate = await new UnavailableClimateDataProvider().ResolveAsync(latitude, longitude, TestContext.Current.CancellationToken);

        flood.Type.Should().Be(DataResolutionOutcomeType.Unavailable);
        climate.Type.Should().Be(DataResolutionOutcomeType.Unavailable);
    }
}
