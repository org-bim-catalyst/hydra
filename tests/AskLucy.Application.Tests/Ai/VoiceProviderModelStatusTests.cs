using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;
using AskLucy.Application.Ai.Queries.GetAdminVoiceProviders;
using AskLucy.Application.Ai.Queries.GetVoiceEngines;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/072 FR-036, FR-037, FR-039 — the admin Voice page follows the custom model that
/// backs an on-server engine; engines that need an API key are unaffected.</summary>
public sealed class VoiceProviderModelStatusTests
{
    private const string SupertonicRepository = "Supertone/supertonic-3";
    private const string UnavailableReason = "The Supertonic model is marked unavailable in Custom Models.";

    private readonly IVoiceProviderRepository _repository = Substitute.For<IVoiceProviderRepository>();
    private readonly IHostedModelLocator _locator = Substitute.For<IHostedModelLocator>();
    private readonly ITextToSpeechEngine _elevenLabs = Substitute.For<ITextToSpeechEngine>();
    private readonly ITextToSpeechEngine _supertonic = Substitute.For<ITextToSpeechEngine, IHostedModelEngine>();
    private readonly List<VoiceProvider> _rows = [];

    public VoiceProviderModelStatusTests()
    {
        _elevenLabs.ProviderKey.Returns("ElevenLabs");
        _elevenLabs.DisplayName.Returns("ElevenLabs");
        _elevenLabs.RequiresCredential.Returns(true);

        _supertonic.ProviderKey.Returns("Supertonic");
        _supertonic.DisplayName.Returns("Supertonic (on-server)");
        _supertonic.RequiresCredential.Returns(false);
        Hosted.EngineName.Returns("Supertonic");
        Hosted.ModelRepositoryId.Returns(SupertonicRepository);
        Hosted.FindModelProblemAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        _locator.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(HostedModelResolution.NoRecord.Instance);
        _repository.ListByPriorityAsync(Arg.Any<CancellationToken>()).Returns(_ => _rows.OrderBy(r => r.Priority).ToList());
    }

    private IHostedModelEngine Hosted => (IHostedModelEngine)_supertonic;

    private ITextToSpeechEngine[] Engines => [_elevenLabs, _supertonic];

    private void AddRow(string key, int priority) => _rows.Add(VoiceProvider.Create(key, key, priority, "system"));

    private Task<IReadOnlyList<VoiceEngineDto>> ListEnginesAsync() =>
        new GetVoiceEnginesQueryHandler(_repository, Engines, _locator).Handle(new GetVoiceEnginesQuery(), CancellationToken.None);

    private Task<IReadOnlyList<AdminVoiceProviderDto>> ListProvidersAsync() =>
        new GetAdminVoiceProvidersQueryHandler(_repository, Engines).Handle(new GetAdminVoiceProvidersQuery(), CancellationToken.None);

    [Fact]
    public async Task GetVoiceEngines_ShouldLeaveOutAnOnServerEngine_WhileItsCustomModelIsUnavailable()
    {
        _locator.ResolveAsync(SupertonicRepository, Arg.Any<CancellationToken>())
            .Returns(new HostedModelResolution.Unavailable("unavailable"));

        var engines = await ListEnginesAsync();

        engines.Select(e => e.ProviderKey).Should().Equal(["ElevenLabs"], "an API-key engine is never affected (FR-036)");
    }

    [Fact]
    public async Task GetVoiceEngines_ShouldOfferAnOnServerEngine_WithNoCompletedRecord()
    {
        // NoRecord also covers a first deployment that is still running, failed or was cancelled:
        // the locator only counts Completed records (FR-039).
        var engines = await ListEnginesAsync();

        engines.Select(e => e.ProviderKey).Should().Equal("ElevenLabs", "Supertonic");
        await _locator.Received(1).ResolveAsync(SupertonicRepository, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVoiceEngines_ShouldOfferAnOnServerEngine_WhenItsCustomModelIsAvailable()
    {
        _locator.ResolveAsync(SupertonicRepository, Arg.Any<CancellationToken>())
            .Returns(new HostedModelResolution.Available("Models/supertonic-3", Guid.NewGuid()));
        AddRow("Supertonic", 0);

        var engines = await ListEnginesAsync();

        engines.Should().ContainSingle(e => e.ProviderKey == "Supertonic").Which.IsAdded.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdminVoiceProviders_ShouldReportModelUnavailable_WithTheEnginesReason()
    {
        AddRow("Supertonic", 0);
        AddRow("ElevenLabs", 1);
        Hosted.FindModelProblemAsync(Arg.Any<CancellationToken>()).Returns(UnavailableReason);

        var providers = await ListProvidersAsync();

        var supertonic = providers.Single(p => p.ProviderKey == "Supertonic");
        supertonic.ModelStatus.Should().Be(VoiceProviderModelStatus.ModelUnavailable);
        supertonic.ModelStatusReason.Should().Be(UnavailableReason);
        supertonic.IsPrimary.Should().BeTrue("the row is kept and keeps its place (FR-037)");

        var elevenLabs = providers.Single(p => p.ProviderKey == "ElevenLabs");
        elevenLabs.ModelStatus.Should().Be(VoiceProviderModelStatus.Ready);
        elevenLabs.ModelStatusReason.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminVoiceProviders_ShouldReportReady_WhenTheHostedModelCanLoad()
    {
        AddRow("Supertonic", 0);

        var provider = (await ListProvidersAsync()).Should().ContainSingle().Subject;

        provider.ModelStatus.Should().Be(VoiceProviderModelStatus.Ready);
        provider.ModelStatusReason.Should().BeNull();
    }

    [Fact]
    public async Task SetPrimaryVoiceProvider_ShouldReturnRowsWithTheirModelStatus()
    {
        AddRow("ElevenLabs", 0);
        AddRow("Supertonic", 1);
        var supertonicId = _rows.Single(r => r.ProviderKey == "Supertonic").Id;
        Hosted.FindModelProblemAsync(Arg.Any<CancellationToken>()).Returns(UnavailableReason);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns("admin-1");
        var handler = new SetPrimaryVoiceProviderCommandHandler(
            _repository, Engines, Substitute.For<IUnitOfWork>(), currentUser, NullLogger<SetPrimaryVoiceProviderCommandHandler>.Instance);

        var rows = await handler.Handle(new SetPrimaryVoiceProviderCommand(supertonicId, "F1"), CancellationToken.None);

        rows[0].ProviderKey.Should().Be("Supertonic");
        rows[0].ModelStatus.Should().Be(VoiceProviderModelStatus.ModelUnavailable);
        rows[1].ModelStatus.Should().Be(VoiceProviderModelStatus.Ready);
    }
}
