using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// specs/042-site-boundary-resolution T047 — the secondary `IAgentTool` surface (research.md #11):
/// input validation, geocoding top-candidate selection, and each outcome→JSON mapping.
/// </summary>
public sealed class SiteBoundaryResolverToolTests
{
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly IBoundaryResolutionService _boundaryResolutionService = Substitute.For<IBoundaryResolutionService>();
    private readonly SiteBoundaryResolverTool _tool;

    public SiteBoundaryResolverToolTests()
    {
        _tool = new SiteBoundaryResolverTool(_geocodingProvider, _boundaryResolutionService);
    }

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), UserChatId: Guid.NewGuid());

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenLocationQueryIsMissing()
    {
        using var input = JsonDocument.Parse("{}");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnConfirmed_WhenAHighConfidenceBoundaryIsResolved()
    {
        _geocodingProvider.SearchAsync("Al Safa Park 2", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate> { new("Al Safa Park 2", 25.156, 55.2218, 0.9) });

        var boundary = new ConfirmedSiteBoundaryData(
            "Al Safa Park 2", 25.156, 55.2218,
            [new GeoPoint(25.156, 55.221), new GeoPoint(25.156, 55.222), new GeoPoint(25.155, 55.222)],
            15_000, 0.92, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", []);
        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, boundary, "I've outlined Al Safa Park 2's boundary."));

        using var input = JsonDocument.Parse("""{"locationQuery":"Al Safa Park 2"}""");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("outcome").GetString().Should().Be("confirmed");
        var boundaryElement = result.Output.RootElement.GetProperty("boundary");
        boundaryElement.GetProperty("siteName").GetString().Should().Be("Al Safa Park 2");
        boundaryElement.GetProperty("confidenceLevel").GetString().Should().Be("high");
        boundaryElement.GetProperty("source").GetString().Should().Be("osm-boundary");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnNotFound_WhenGeocodingFindsNothing()
    {
        _geocodingProvider.SearchAsync("NowhereLand99", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>());

        using var input = JsonDocument.Parse("""{"locationQuery":"NowhereLand99"}""");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("outcome").GetString().Should().Be("not_found");
        await _boundaryResolutionService.DidNotReceive().ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnUnavailable_WhenGeocodingProviderThrows()
    {
        _geocodingProvider.SearchAsync("Al Safa Park 2", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<GeocodingCandidate>>(new GeocodingProviderUnavailableException("down")));

        using var input = JsonDocument.Parse("""{"locationQuery":"Al Safa Park 2"}""");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("outcome").GetString().Should().Be("unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnUnavailable_WhenBoundaryResolutionIsUnavailable()
    {
        _geocodingProvider.SearchAsync("Al Safa Park 2", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate> { new("Al Safa Park 2", 25.156, 55.2218, 0.9) });
        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, BoundaryConfirmationTemplates.Unavailable));

        using var input = JsonDocument.Parse("""{"locationQuery":"Al Safa Park 2"}""");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Output!.RootElement.GetProperty("outcome").GetString().Should().Be("unavailable");
        result.Output.RootElement.TryGetProperty("boundary", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPickTheHighestImportanceCandidate_WhenGeocodingReturnsSeveral()
    {
        _geocodingProvider.SearchAsync("Springfield", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Springfield, MA", 42.1015, -72.5898, 0.4),
                new("Springfield, IL", 39.7817, -89.6501, 0.9),
            });
        var boundary = new ConfirmedSiteBoundaryData(
            "Springfield, IL", 39.7817, -89.6501,
            [new GeoPoint(39.78, -89.65), new GeoPoint(39.79, -89.65), new GeoPoint(39.78, -89.64)],
            5_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []);
        _boundaryResolutionService.ResolveAsync(Arg.Is<ConfirmedLocationData>(l => l != null && l.LocationName == "Springfield, IL"), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, boundary, "I've outlined Springfield, IL's boundary."));

        using var input = JsonDocument.Parse("""{"locationQuery":"Springfield"}""");

        var result = await _tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Output!.RootElement.GetProperty("boundary").GetProperty("siteName").GetString().Should().Be("Springfield, IL");
    }
}
