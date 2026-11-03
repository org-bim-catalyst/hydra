using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>specs/057-site-analysis-agent tasks.md T039 — dispatch never awaits the workflow (SC-001), explicit coordinates skip geocoding (FR-001), a second request reuses the in-flight analysis, and an unresolvable site fails in-turn (FR-023).</summary>
public sealed class RequestSiteAnalysisCapabilityTests
{
    private const string UserId = "user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ISiteAnalysisRepository _siteAnalysisRepository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly IBoundaryResolutionService _boundaryResolutionService = Substitute.For<IBoundaryResolutionService>();
    private readonly ISiteAnalysisDispatcher _dispatcher = Substitute.For<ISiteAnalysisDispatcher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RequestSiteAnalysisCapability BuildCapability() =>
        new(_userChatRepository, _siteAnalysisRepository, _geocodingProvider, _boundaryResolutionService, _dispatcher, _unitOfWork, new EffectiveSiteBoundary(Substitute.For<ISiteBoundaryCorrectionRepository>()));

    private static AgentToolExecutionContext ContextFor(Guid? userChatId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), userChatId);

    [Fact]
    public void ArgumentHint_ShouldTellTheModelToPassANamedSite_AndReserveOmissionForTheActiveSite()
    {
        // specs/090 follow-up — "Run a site analysis for Al Safa Park 2" started an analysis for
        // the site already on screen, because the hint allowed omitting a place the user typed.
        var capability = BuildCapability();

        capability.ArgumentHint.Should().Contain("the name the user asked for");
        capability.ArgumentHint.Should().Contain("'this site'");
        capability.ArgumentHint.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturn_WithoutAwaitingTheWorkflowItself_WhenExplicitCoordinatesAreGiven()
    {
        _siteAnalysisRepository.FindRunningForSiteAsync(UserId, UserChatId, "the specified location", Arg.Any<CancellationToken>())
            .Returns((Domain.SiteAnalysis.SiteAnalysis?)null);
        _dispatcher.DispatchAsync(Arg.Any<Guid>(), UserId, Arg.Any<string>(), Arg.Any<string>(), 25.09, 55.20, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var capability = BuildCapability();
        var input = JsonDocument.Parse("""{"latitude":25.09,"longitude":55.20}""");
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();

        // FR-001 — coordinates skip geocoding entirely.
        await _geocodingProvider.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
        await _dispatcher.Received(1).DispatchAsync(Arg.Any<Guid>(), UserId, Arg.Any<string>(), Arg.Any<string>(), 25.09, 55.20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseTheStoredBoundary_WhenTheNamedSiteIsAlreadyTheActiveOne()
    {
        // Observed locally: "Analyze this site" with Al Safa Park 2 selected reached the assistant as
        // site="Al Safa Park 2", re-ran the whole resolution pipeline (geocode, Overpass, imagery,
        // vision) and exhausted the capability's 90 s budget before dispatch. The active site's own
        // boundary is both cheaper and the one the analysis must use (SPEC-042's same-site rule).
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218,
            [new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220), new GeoPoint(25.1550, 55.2220)],
            areaSquareMeters: 6_000, confidence: 0.9, BoundaryConfidenceLevel.High,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var capability = BuildCapability();
        var input = JsonDocument.Parse("""{"site":"Al Safa Park 2"}""");
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();

        await _geocodingProvider.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
        await _boundaryResolutionService.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, TestContext.Current.CancellationToken);
        await _dispatcher.Received(1).DispatchAsync(
            Arg.Any<Guid>(), UserId, "Al Safa Park 2", "Al Safa Park 2", 25.156, 55.2218, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("this site")]
    [InlineData("the current site")]
    [InlineData("this place")]
    [InlineData("here")]
    public async Task ExecuteAsync_ShouldTreatAPhraseForTheActiveSite_AsTheActiveSite(string phrase)
    {
        // The deciding model passes the user's literal words instead of omitting the argument.
        // Geocoding "this site" once ran the whole boundary pipeline and timed the turn out.
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218,
            [new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220), new GeoPoint(25.1550, 55.2220)],
            areaSquareMeters: 6_000, confidence: 0.9, BoundaryConfidenceLevel.High,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var capability = BuildCapability();
        var input = JsonDocument.Parse($$"""{"site":"{{phrase}}"}""");
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();
        result.Output.RootElement.GetProperty("siteName").GetString().Should().Be("Al Safa Park 2");
        await _geocodingProvider.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("""{"site":null}""")]
    [InlineData("""{"site":""}""")]
    [InlineData("""{"site":"   "}""")]
    public async Task ExecuteAsync_ShouldUseTheActiveSite_WhenTheSiteArgumentIsNotNullOrEmpty(string json)
    {
        // The deciding model sets the property to null (or an empty string) instead of omitting it.
        // Calling GetString() on a JSON null threw and surfaced as a bare "value provided was null".
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.156, 55.2218,
            [new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220), new GeoPoint(25.1550, 55.2220)],
            areaSquareMeters: 6_000, confidence: 0.9, BoundaryConfidenceLevel.High,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), JsonDocument.Parse(json), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();
        result.Output.RootElement.GetProperty("siteName").GetString().Should().Be("Al Safa Park 2");
        await _geocodingProvider.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStillResolveGenuineSiteNames_ByGeocoding()
    {
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var capability = BuildCapability();
        var input = JsonDocument.Parse("""{"site":"Burj Khalifa"}""");
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), input, CancellationToken.None);

        // Nothing found, so the in-turn "site not identified" outcome — but the name was looked up,
        // not silently treated as the (absent) active site.
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeFalse();
        await _geocodingProvider.Received(1).SearchAsync("Burj Khalifa", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailInTurn_WhenNoSiteCanBeIdentified()
    {
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(UserChat.Create("Chat", UserId, null, UserId));

        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var output = result.Output!.RootElement;
        output.GetProperty("started").GetBoolean().Should().BeFalse();
        output.GetProperty("reason").GetString().Should().Be("site-not-identified");
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseTheInFlightAnalysis_RatherThanDispatchingATwin()
    {
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveLocation(25.09, 55.20, "Al Barsha South", 0.9, UserId);
        _userChatRepository.GetByIdAsync(UserChatId, Arg.Any<CancellationToken>()).Returns(chat);

        var existing = Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Al Barsha South", 25.09, 55.20, null, 1, UserId);
        _siteAnalysisRepository.FindRunningForSiteAsync(UserId, UserChatId, "Al Barsha South", Arg.Any<CancellationToken>()).Returns(existing);

        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(UserChatId), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailInTurn_WithoutStartingAnAnalysis_WhenThereIsNoConversation()
    {
        var capability = BuildCapability();
        var result = await capability.ExecuteAsync(ContextFor(null), JsonDocument.Parse("{}"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("started").GetBoolean().Should().BeFalse();
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default, default!, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void IsOfferable_ShouldFollowTheSite_NotAvailability()
    {
        // specs/068. Previously never offered, on the reasoning that Lucy runs it when asked --
        // which answers a different question. Offering it is for the user who has NOT asked,
        // because they do not know it exists; and an offer is the right way to reach something
        // this expensive, since the user consents before it spends the time.
        var capability = BuildCapability();
        var empty = TurnContext.Empty(UserId, UserChatId);
        var located = empty with { ActiveLocation = new ActiveSiteLocation(25.156, 55.2218, "Al Safa Park 2", 0.9) };

        capability.IsAvailable(empty).Should().BeTrue("a missing site is narrated, not hidden (FR-023)");
        capability.IsOfferable(empty, TurnOutcome.None).Should().BeFalse("there is nothing to offer analysis of");
        capability.IsOfferable(located, TurnOutcome.None).Should().BeTrue();

        var justRan = new TurnOutcome([RequestSiteAnalysisCapability.CapabilityKey], false, [], false);
        capability.IsOfferable(located, justRan).Should().BeFalse(
            "an offer must never propose work the user just watched start");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateTheAnalysisWithTheDeclaredSpecialistCount()
    {
        // specs/088 FR-003/FR-004 — the expected count is derived from the same declaration the
        // provisioner publishes branches from. A hand-maintained constant here silently closes the
        // analysis after the first specialist reports, and later findings are rejected.
        _siteAnalysisRepository
            .FindRunningForSiteAsync(UserId, UserChatId, "the specified location", Arg.Any<CancellationToken>())
            .Returns((Domain.SiteAnalysis.SiteAnalysis?)null);
        _dispatcher
            .DispatchAsync(Arg.Any<Guid>(), UserId, Arg.Any<string>(), Arg.Any<string>(), 25.09, 55.20, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        Domain.SiteAnalysis.SiteAnalysis? created = null;
        _siteAnalysisRepository
            .When(repository => repository.Add(Arg.Any<Domain.SiteAnalysis.SiteAnalysis>()))
            .Do(call => created = call.Arg<Domain.SiteAnalysis.SiteAnalysis>());

        var capability = BuildCapability();
        await capability.ExecuteAsync(
            ContextFor(UserChatId), JsonDocument.Parse("""{"latitude":25.09,"longitude":55.20}"""), TestContext.Current.CancellationToken);

        created.Should().NotBeNull();
        created!.ExpectedResultCount.Should().Be(SiteAnalysisSpecialists.Count);
    }
}
