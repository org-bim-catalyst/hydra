using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Application.Tests.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>
/// specs/093 end-to-end over the SPEC-094 path: the capability's operation JSON is interpreted by
/// the coordinator, dispatched to the proximity specialist, normalized, and serialized into the
/// viewer command — the whole chain except the SSE write and the browser.
/// </summary>
public sealed class SpatialCapabilityProximityFlowTests
{
    private static readonly Guid ChatId = Guid.NewGuid();

    private readonly IPoiQueryProvider _provider = Substitute.For<IPoiQueryProvider>();
    private readonly ILocationResolutionService _geocoder = Substitute.For<ILocationResolutionService>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly SpatialLayerStateStore _store = new(TimeProvider.System);

    private SpatialAnalysisCapability Build()
    {
        var catalog = new PoiCategoryCatalog();
        var coordinator = new SpatialAnalysisCoordinator(
            new SpatialSpecialistRegistry([new ProximityAnalysisSpecialist(_provider, catalog)]),
            new SpatialBundleValidator(),
            new SpatialResultNormalizer(),
            new StudyAreaResolver(_geocoder, Substitute.For<IUserChatRepository>(), TestEffectiveSiteBoundary.None()),
            _store,
            TimeProvider.System,
            NullLogger<SpatialAnalysisCoordinator>.Instance);

        return new SpatialAnalysisCapability(coordinator, _store, catalog, _messages);
    }

    private void GivenMuscatResolves()
    {
        _geocoder.ResolveQueryAsync(ChatId, "Muscat", Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(
                    23.588, 58.382, "Muscat", 0.9,
                    Viewport: new ViewportBounds(23.65, 58.45, 23.52, 58.31)),
                null));

        _provider.QueryAsync(Arg.Is<PoiQuery>(q => q.Category.Key == "school"), Arg.Any<CancellationToken>())
            .Returns([
                new PoiFeature("osm_s1", "Near School", 23.59, 58.385, new Dictionary<string, string>()),
                new PoiFeature("osm_s2", "Far School", 23.7, 58.5, new Dictionary<string, string>()),
            ]);

        _provider.QueryAsync(Arg.Is<PoiQuery>(q => q.Category.Key == "hospital"), Arg.Any<CancellationToken>())
            .Returns([
                new PoiFeature("osm_h1", "Central Hospital", 23.5885, 58.3825, new Dictionary<string, string>()),
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeliverWithinMarkersAndBuffers_ForAProximityOperation()
    {
        GivenMuscatResolves();
        var input = JsonDocument.Parse(
            """
            {"operations":[{"kind":"proximity.analysis","category":"schools","targetCategory":"hospitals",
              "distance":1000,"unit":"m","area":"Muscat"}]}
            """);

        var result = await Build().ExecuteAsync(
            new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), ChatId),
            input,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        var root = result.Output!.RootElement;
        root.GetProperty("status").GetString().Should().Be("Complete");
        root.GetProperty("count").GetInt32().Should().Be(1);

        var layers = root.GetProperty("layers").EnumerateArray().ToList();
        var markers = layers.Single(layer => layer.GetProperty("kind").GetString() == "markers");
        markers.GetProperty("markers").EnumerateArray().Select(feature => feature.GetProperty("id").GetString())
            .Should().Equal("osm_s1");
        layers.Should().Contain(layer => layer.GetProperty("kind").GetString() == "buffer");
        root.GetProperty("areaName").GetString().Should().Be("Muscat");
    }
}
