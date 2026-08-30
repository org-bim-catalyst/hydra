using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Locations;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Locations;

/// <summary>
/// specs/037-location-query-resolution T015 — <see cref="LocationResolutionService"/> routes
/// each intent/geocoding combination to the correct <see cref="LocationResolutionOutcomeType"/>
/// and never throws (constitution §2.VIII). All paths verified against the determinance-margin
/// algorithm and WGS-84 range validation.
/// </summary>
public sealed class LocationResolutionServiceTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly LocationResolutionService _service;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;

    private static readonly Guid ChatId = Guid.NewGuid();

    public LocationResolutionServiceTests()
    {
        _openAiProvider = AIProvider.Create("openai", "OpenAI", "test");
        _openAiProvider.SetCredential("ciphertext", "test");
        _openAiProvider.Enable("test");

        _gpt41 = AIModel.Create(
            _openAiProvider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { _openAiProvider });
        _providers.GetByIdAsync(_openAiProvider.Id, Arg.Any<CancellationToken>()).Returns(_openAiProvider);
        _models.GetByIdAsync(_gpt41.Id, Arg.Any<CancellationToken>()).Returns(_gpt41);
        _models.ListAvailableByProviderIdAsync(_openAiProvider.Id, Arg.Any<CancellationToken>()).Returns(new List<AIModel> { _gpt41 });
        _providerResolver.Resolve("openai").Returns(_aiProvider);

        var defaultResolver = new DefaultProviderResolver(_providers, _models);
        _service = new LocationResolutionService(
            defaultResolver, _providers, _models, _providerResolver,
            _geocodingProvider, Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            Substitute.For<ILogger<LocationResolutionService>>());
    }

    private void StubClassification(string intent, params string[] placeQueries)
    {
        var queries = string.Join(",", placeQueries.Select(q => $"\"{q}\""));
        var json = $"{{\"intent\":\"{intent}\",\"placeQueries\":[{queries}]}}";
        _aiProvider.ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(),
            Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(json, new ChatUsage(10, 5, null, null, null)));
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNoIntent_WhenClassifierReturnsNone()
    {
        StubClassification("none");

        var outcome = await _service.ResolveAsync("user-1", ChatId, "I read about Paris", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.NoIntent);
        outcome.ConfirmedLocation.Should().BeNull();
        outcome.ConfirmationText.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnConfirmed_WhenSingleCandidateAboveFloor()
    {
        StubClassification("new_query", "Dubai");
        _geocodingProvider.SearchAsync("Dubai", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Dubai, UAE", 25.2048, 55.2708, 0.85)
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Dubai", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Confirmed);
        outcome.ConfirmedLocation.Should().NotBeNull();
        outcome.ConfirmedLocation!.LocationName.Should().Be("Dubai");
        outcome.ConfirmedLocation.Latitude.Should().Be(25.2048);
        outcome.ConfirmationText.Should().Contain("Dubai");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnConfirmed_WhenLeaderDominatesSecond()
    {
        StubClassification("new_query", "London");
        _geocodingProvider.SearchAsync("London", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("London, UK", 51.5074, -0.1278, 0.9),
                new("London, Ontario, Canada", 42.9849, -81.2453, 0.5) // margin 0.4 >= 0.2
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Where is London?", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Confirmed);
        outcome.ConfirmedLocation!.LocationName.Should().Be("London");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnAmbiguous_WhenTwoCandidatesAreCloseInImportance()
    {
        StubClassification("new_query", "Springfield");
        _geocodingProvider.SearchAsync("Springfield", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Springfield, IL", 39.7817, -89.6501, 0.75),
                new("Springfield, MA", 42.1015, -72.5898, 0.70) // margin 0.05 < 0.2
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Springfield", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Ambiguous);
        outcome.ConfirmedLocation.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnAmbiguous_WhenClassifierReturnsTwoPlaces()
    {
        StubClassification("new_query", "Dubai", "Abu Dhabi");

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Compare Dubai and Abu Dhabi", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Ambiguous);
        await _geocodingProvider.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNotFound_WhenNoResultsAboveImportanceFloor()
    {
        StubClassification("new_query", "NowhereLand99");
        _geocodingProvider.SearchAsync("NowhereLand99", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Something Weak", 0, 0, 0.0) // below MinimumImportanceFloor=0.05
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me NowhereLand99", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.NotFound);
    }

    /// <summary>
    /// Regression — "show me Al Safa Park 2 in the viewer" stopped moving the viewer entirely.
    /// The cause was upstream of every boundary/streaming fix attempted for it: Nominatim scores
    /// that park at importance 0.0801, the floor was 0.1, so the only correct candidate was
    /// filtered out and the turn resolved to NotFound — no ConfirmedLocation, therefore no
    /// __LOCATION__ event and no boundary step. Production never showed it because a configured
    /// Geocoding:GoogleMapsApiKey routes to the Google adapter, whose synthesised importance
    /// never drops below 0.40. Pinned with the real observed value.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldConfirm_WhenSoleCandidateIsAnOrdinaryLocalPlace()
    {
        StubClassification("new_query", "Al Safa Park 2");
        _geocodingProvider.SearchAsync("Al Safa Park 2", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("حديقة الصفا 2, دبي", 25.1556657, 55.2219295, 0.0801)
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Al Safa Park 2 in the viewer", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Confirmed);
        outcome.ConfirmedLocation!.Latitude.Should().BeApproximately(25.1556657, 0.0001);
        outcome.ConfirmedLocation.Longitude.Should().BeApproximately(55.2219295, 0.0001);
    }

    /// <summary>
    /// The floor still has to earn its place: Nominatim returns importance 0.0 for a result that
    /// merely shares a name (observed live — several unrelated streets named "Dubai Mall").
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldIgnoreZeroImportanceNameCollisions_AndConfirmTheRealPlace()
    {
        StubClassification("new_query", "Dubai Mall");
        _geocodingProvider.SearchAsync("Dubai Mall", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Dubai Mall, Downtown Dubai", 25.1972, 55.2796, 0.4451),
                new("Dubai Mall, Kozhikode, India", 11.2588, 75.7804, 0.0),
                new("Dubai Mall, Belbeis Road, Egypt", 30.4, 31.5, 0.0)
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Dubai Mall", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Confirmed);
        outcome.ConfirmedLocation!.Latitude.Should().BeApproximately(25.1972, 0.0001);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNotFound_WhenGeocodingReturnsEmpty()
    {
        StubClassification("new_query", "VoidTown");
        _geocodingProvider.SearchAsync("VoidTown", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>());

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me VoidTown", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.NotFound);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNotFound_WhenWinnerCoordinatesAreOutOfWgs84Range()
    {
        StubClassification("new_query", "BadCoord");
        _geocodingProvider.SearchAsync("BadCoord", Arg.Any<CancellationToken>())
            .Returns(new List<GeocodingCandidate>
            {
                new("Out of Range Place", 200.0, 55.0, 0.9) // lat 200 > 90
            });

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me BadCoord", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.NotFound);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenGeocodingProviderThrows()
    {
        StubClassification("new_query", "Dubai");
        _geocodingProvider.SearchAsync("Dubai", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<GeocodingCandidate>>(
                new GeocodingProviderUnavailableException("Nominatim down.")));

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Dubai", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Unavailable);
        outcome.ConfirmationText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenAiProviderThrows()
    {
        _aiProvider.ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(),
            Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChatCompletionResult>(new AiProviderUnavailableException("down")));

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Dubai", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Unavailable);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenClassifierResponseIsNotJson()
    {
        _aiProvider.ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(),
            Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("not valid json at all", new ChatUsage(10, 5, null, null, null)));

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Show me Dubai", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Unavailable);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReEmitActiveLocation_WhenBackReferenceWithExistingLocation()
    {
        StubClassification("back_reference");
        var existing = new ActiveSiteLocation(25.2048, 55.2708, "Dubai, UAE", 0.85);

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Zoom in on it", existing);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Confirmed);
        outcome.ConfirmedLocation!.Latitude.Should().Be(25.2048);
        outcome.ConfirmedLocation.LocationName.Should().Be("Dubai, UAE");
        await _geocodingProvider.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailableWithExplanation_WhenBackReferenceButNoActiveLocation()
    {
        StubClassification("back_reference");

        var outcome = await _service.ResolveAsync("user-1", ChatId, "Go there", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Unavailable);
        outcome.ConfirmationText.Should().Be(LocationConfirmationTemplates.BackReferenceNoActive);
    }

    // T024 — Phase 4 (US2): NoIntent path
    [Fact]
    public async Task ResolveAsync_ShouldReturnNoIntent_WithNullConfirmationText_WhenClassifierReturnsNone()
    {
        StubClassification("none");

        var outcome = await _service.ResolveAsync("user-1", ChatId, "I read that Al Safa Park was renovated", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.NoIntent);
        outcome.ConfirmationText.Should().BeNull();
        outcome.ConfirmedLocation.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotCallGeocodingProvider_WhenNoIntent()
    {
        StubClassification("none");

        await _service.ResolveAsync("user-1", ChatId, "compare parking ratios", null);

        await _geocodingProvider.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // T026 — Phase 5 (US3): Ambiguous/NotFound/Unavailable confirmed paths
    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenIntentIsUnrecognized()
    {
        _aiProvider.ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(),
            Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("{\"intent\":\"maybe\",\"placeQueries\":[]}", new ChatUsage(5, 5, null, null, null)));

        var outcome = await _service.ResolveAsync("user-1", ChatId, "maybe show Dubai?", null);

        outcome.Type.Should().Be(LocationResolutionOutcomeType.Unavailable);
        outcome.ConfirmationText.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(LocationResolutionOutcomeType.Ambiguous)]
    [InlineData(LocationResolutionOutcomeType.NotFound)]
    [InlineData(LocationResolutionOutcomeType.Unavailable)]
    public async Task ResolveAsync_ShouldHaveNullConfirmedLocation_ForAllNonConfirmedOutcomes(LocationResolutionOutcomeType expectedType)
    {
        switch (expectedType)
        {
            case LocationResolutionOutcomeType.Ambiguous:
                StubClassification("new_query", "Place A", "Place B");
                break;
            case LocationResolutionOutcomeType.NotFound:
                StubClassification("new_query", "VoidTown");
                _geocodingProvider.SearchAsync("VoidTown", Arg.Any<CancellationToken>()).Returns(new List<GeocodingCandidate>());
                break;
            case LocationResolutionOutcomeType.Unavailable:
                StubClassification("new_query", "Dubai");
                _geocodingProvider.SearchAsync("Dubai", Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<IReadOnlyList<GeocodingCandidate>>(
                        new GeocodingProviderUnavailableException("down")));
                break;
        }

        var outcome = await _service.ResolveAsync("user-1", ChatId, "test message", null);

        outcome.Type.Should().Be(expectedType);
        outcome.ConfirmedLocation.Should().BeNull();
        outcome.ConfirmationText.Should().NotBeNullOrEmpty();
    }
}
