using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.Ai.Dictation.Commands.SelectLocalWhisperModel;
using AskLucy.Application.Ai.Dictation.Commands.TryLocalWhisperModel;
using AskLucy.Application.Ai.Dictation.Queries.GetDictationSettings;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AskLucy.Application.Tests.Ai.Dictation;

/// <summary>specs/078 T026 — reading the dictation setting, selecting and trying a Local Whisper model.</summary>
public sealed class LocalWhisperModelAdminTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] RowVersion = [0, 0, 0, 0, 0, 0, 7, 209];

    private readonly DictationEngineSetting _setting = DictationEngineSetting.CreateDefault(Now);
    private readonly IDictationEngineSettingRepository _settings = Substitute.For<IDictationEngineSettingRepository>();
    private readonly ILocalWhisperModelCatalog _catalog = Substitute.For<ILocalWhisperModelCatalog>();
    private readonly ILocalWhisperModelTrial _trial = Substitute.For<ILocalWhisperModelTrial>();
    private readonly IAIProviderRepository _aiProviders = Substitute.For<IAIProviderRepository>();
    private readonly ISpeechToTextSessionProvider _elevenLabs = Substitute.For<ISpeechToTextSessionProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public LocalWhisperModelAdminTests()
    {
        _setting.RowVersion = RowVersion;
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(_setting);
        _currentUser.UserId.Returns("admin-1");
        _catalog.ListOptionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _catalog.ResolveSelectedAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(LocalWhisperModelResolution.None.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetDictationSettings_ListsModels_WithTheirReasons_AndTheVendorGates()
    {
        var ready = new LocalWhisperModelOption(Guid.NewGuid(), "whisper.cpp (ggml-base.bin)", true, null);
        var whole = new LocalWhisperModelOption(Guid.NewGuid(), "supertonic-3", false, "Deploy the model from a URL that names its .bin file.");
        _catalog.ListOptionsAsync(Arg.Any<CancellationToken>()).Returns([ready, whole]);
        var openAi = AIProvider.Create("openai", "OpenAI", "system");
        openAi.SetCredential("ciphertext", null, "system");
        openAi.Enable("system");
        _aiProviders.GetByKeyAsync("openai", Arg.Any<CancellationToken>()).Returns(openAi);
        _elevenLabs.IsSwitchedOnAsync(Arg.Any<CancellationToken>()).Returns(false);

        var dto = await QueryHandler().Handle(new GetDictationSettingsQuery(), Ct);

        dto.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        dto.RowVersion.Should().Be(Convert.ToBase64String(RowVersion));
        dto.LocalWhisper.Models.Should().Equal(ready, whole);
        dto.Engines.Should().Equal(
            new DictationEngineChoiceDto("LocalWhisper", true, null),
            new DictationEngineChoiceDto("OpenAiWhisper", true, null),
            new DictationEngineChoiceDto("ElevenLabsRealtime", false, DictationVendorGate.ElevenLabsSwitchedOff));
        dto.PushToTalkEngines.Select(e => e.Engine).Should().Equal("LocalWhisper", "OpenAiWhisper", "Browser");
        dto.Suspension.Should().BeNull();
        dto.LastRevert.Should().BeNull();
    }

    [Fact]
    public async Task GetDictationSettings_OpenAiWithoutARow_IsSwitchedOff()
    {
        var dto = await QueryHandler().Handle(new GetDictationSettingsQuery(), Ct);

        dto.Engines.Single(e => e.Engine == "OpenAiWhisper").UnavailableReason.Should().Be(DictationVendorGate.OpenAiSwitchedOff);
        dto.PushToTalkEngines.Single(e => e.Engine == "OpenAiWhisper").Selectable.Should().BeFalse();
    }

    [Fact]
    public async Task GetDictationSettings_NoModelSelected_PointsTheAdminToCustomModels()
    {
        var dto = await QueryHandler().Handle(new GetDictationSettingsQuery(), Ct);

        dto.LocalWhisper.SelectedModelId.Should().BeNull();
        dto.LocalWhisper.EffectiveModel.Should().Be(new LocalWhisperEffectiveModelDto(null, false, GetDictationSettingsQueryHandler.NoModelProblem));
        dto.LocalWhisper.EffectiveModel.Problem.Should().Contain("ggml-base.bin");
    }

    [Fact]
    public async Task GetDictationSettings_ExplainsAnUnavailableOrBrokenModel_AndIsQuietWhenReady()
    {
        _catalog.ResolveSelectedAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(
            new LocalWhisperModelResolution.Unavailable("The selected model is marked Unavailable under Custom Models.", "m"),
            new LocalWhisperModelResolution.Broken("The model file is missing.", "m"),
            new LocalWhisperModelResolution.Ready("C:/x/ggml-base.bin", "m", "ggml-base.bin"));
        var handler = QueryHandler();

        (await handler.Handle(new GetDictationSettingsQuery(), Ct)).LocalWhisper.EffectiveModel.Problem
            .Should().StartWith("The selected model is marked Unavailable").And.Contain("browser built-in");
        (await handler.Handle(new GetDictationSettingsQuery(), Ct)).LocalWhisper.EffectiveModel.Problem
            .Should().StartWith("The model file is missing.");
        (await handler.Handle(new GetDictationSettingsQuery(), Ct)).LocalWhisper.EffectiveModel
            .Should().Be(new LocalWhisperEffectiveModelDto("m", true, null));
    }

    [Fact]
    public async Task GetDictationSettings_ReportsASuspension_WithTheBrowserInUse()
    {
        _setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin-1", Now);
        _setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now);

        var dto = await QueryHandler().Handle(new GetDictationSettingsQuery(), Ct);

        dto.State.Should().Be(DictationEngineState.Suspended);
        dto.Suspension.Should().Be(new DictationSuspensionDto(DictationPrimaryEngine.OpenAiWhisper, Now, "Quota exhausted", true));
    }

    [Fact]
    public async Task SelectLocalWhisperModel_RefusesAnUnselectableModel_WithItsReason()
    {
        var id = Guid.NewGuid();
        _catalog.CheckSelectableAsync(id, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelOption(id, "supertonic-3", false, "Deploy the model from a URL that names its .bin file."));

        var act = () => SelectHandler().Handle(new SelectLocalWhisperModelCommand(id, Convert.ToBase64String(RowVersion)), Ct);

        await act.Should().ThrowAsync<LocalWhisperModelNotSelectableException>().WithMessage("Deploy the model from a URL that names its .bin file.");
        _setting.LocalWhisperModelId.Should().BeNull();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task SelectLocalWhisperModel_SelectsAModel_AndNullSelectsNone()
    {
        var id = Guid.NewGuid();
        _catalog.CheckSelectableAsync(id, Arg.Any<CancellationToken>()).Returns(new LocalWhisperModelOption(id, "whisper", true, null));
        var handler = SelectHandler();

        await handler.Handle(new SelectLocalWhisperModelCommand(id, Convert.ToBase64String(RowVersion)), Ct);
        _setting.LocalWhisperModelId.Should().Be(id);
        _setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);

        await handler.Handle(new SelectLocalWhisperModelCommand(null, Convert.ToBase64String(RowVersion)), Ct);
        _setting.LocalWhisperModelId.Should().BeNull();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectLocalWhisperModel_AStaleRowVersion_IsAConflict()
    {
        var act = () => SelectHandler().Handle(new SelectLocalWhisperModelCommand(null, Convert.ToBase64String([1, 2, 3])), Ct);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Ct);
    }

    [Fact]
    public void SelectLocalWhisperModelValidator_RequiresARowVersion()
    {
        var validator = new SelectLocalWhisperModelCommandValidator();

        validator.Validate(new SelectLocalWhisperModelCommand(null, "")).IsValid.Should().BeFalse();
        validator.Validate(new SelectLocalWhisperModelCommand(null, "not base64!")).IsValid.Should().BeFalse();
        validator.Validate(new SelectLocalWhisperModelCommand(null, Convert.ToBase64String(RowVersion))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TryLocalWhisperModel_TranscribesOnTheResolvedFile_AndNeverTouchesTheSetting()
    {
        var id = Guid.NewGuid();
        _catalog.ResolveForTrialAsync(id, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("C:/models/ggml-small.bin", "whisper.cpp (ggml-small.bin)", "ggml-small.bin"));
        _trial.TryAsync("C:/models/ggml-small.bin", Arg.Any<Stream>(), "ar", Arg.Any<CancellationToken>())
            .Returns(new DictationTranscript("marhaba", "ar", TimeSpan.FromMilliseconds(1840)));

        var result = await TryHandler().Handle(new TryLocalWhisperModelCommand(id, Wav(), "ar"), Ct);

        result.Should().Be(new LocalWhisperTryResultDto("marhaba", 1840, "whisper.cpp (ggml-small.bin)"));
        await _settings.DidNotReceiveWithAnyArgs().GetOrCreateAsync(Ct);
        _setting.LocalWhisperModelId.Should().BeNull();
    }

    [Fact]
    public async Task TryLocalWhisperModel_WhileAnotherTryRuns_IsBusy()
    {
        var id = Guid.NewGuid();
        _catalog.ResolveForTrialAsync(id, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("C:/m.bin", "m", "m.bin"));
        _trial.TryAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new LocalWhisperTrialBusyException());

        var act = () => TryHandler().Handle(new TryLocalWhisperModelCommand(id, Wav(), null), Ct);

        await act.Should().ThrowAsync<LocalWhisperTrialBusyException>();
    }

    [Fact]
    public async Task TryLocalWhisperModel_ALoadFailure_IsShownToTheAdmin()
    {
        var id = Guid.NewGuid();
        _catalog.ResolveForTrialAsync(id, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("C:/m.bin", "m", "m.bin"));
        _trial.TryAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AiProviderUnavailableException("The Local Whisper model couldn't be loaded."));

        var act = () => TryHandler().Handle(new TryLocalWhisperModelCommand(id, Wav(), null), Ct);

        await act.Should().ThrowAsync<LocalWhisperTrialFailedException>().WithMessage("The Local Whisper model couldn't be loaded.");
    }

    [Fact]
    public async Task TryLocalWhisperModel_RefusesABrokenModel_AndAnInvalidSample()
    {
        var broken = Guid.NewGuid();
        var ready = Guid.NewGuid();
        _catalog.ResolveForTrialAsync(broken, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Broken("The model file is missing.", "m"));
        _catalog.ResolveForTrialAsync(ready, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("C:/m.bin", "m", "m.bin"));

        var brokenAct = () => TryHandler().Handle(new TryLocalWhisperModelCommand(broken, Wav(), null), Ct);
        var webmAct = () => TryHandler().Handle(new TryLocalWhisperModelCommand(ready, new MemoryStream([0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0]), null), Ct);

        await brokenAct.Should().ThrowAsync<LocalWhisperModelNotSelectableException>().WithMessage("The model file is missing.");
        await webmAct.Should().ThrowAsync<DictationAudioInvalidException>();
        await _trial.DidNotReceiveWithAnyArgs().TryAsync(default!, default!, default, Ct);
    }

    private GetDictationSettingsQueryHandler QueryHandler() =>
        new(_settings, _catalog, new DictationVendorGate(_aiProviders, _elevenLabs));

    private SelectLocalWhisperModelCommandHandler SelectHandler() =>
        new(_settings, _catalog, _unitOfWork, _currentUser, TimeProvider.System, NullLogger<SelectLocalWhisperModelCommandHandler>.Instance);

    private TryLocalWhisperModelCommandHandler TryHandler() =>
        new(_catalog, _trial, _currentUser, NullLogger<TryLocalWhisperModelCommandHandler>.Instance);

    /// <summary>A 0.1 s 16 kHz mono 16-bit PCM WAV of silence.</summary>
    private static MemoryStream Wav()
    {
        const int dataLength = 3200;
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLength);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(16000);
            writer.Write(32000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
        }

        stream.Position = 0;
        return stream;
    }
}
