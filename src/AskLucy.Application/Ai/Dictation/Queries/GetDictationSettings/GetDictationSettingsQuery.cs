using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Dictation.Queries.GetDictationSettings;

/// <summary>specs/078 contracts/admin-dictation.md "Read the setting".</summary>
public sealed record GetDictationSettingsQuery : IRequest<DictationSettingsDto>;

public sealed record DictationSettingsDto(
    DictationPrimaryEngine PrimaryEngine,
    DictationClipEngine PushToTalkEngine,
    DictationEngineState State,
    DictationSuspensionDto? Suspension,
    DictationRevertDto? LastRevert,
    IReadOnlyList<DictationEngineChoiceDto> Engines,
    IReadOnlyList<DictationEngineChoiceDto> PushToTalkEngines,
    LocalWhisperSettingsDto LocalWhisper,
    string RowVersion);

/// <summary>While suspended, the browser built-in serves wherever <see cref="Engine"/> would have (FR-016).</summary>
public sealed record DictationSuspensionDto(DictationPrimaryEngine Engine, DateTime AtUtc, string? Reason, bool BrowserInUse);

public sealed record DictationRevertDto(DateTime AtUtc, DictationRevertReason Reason, DictationPrimaryEngine From);

/// <summary><see cref="Engine"/> is a <see cref="DictationPrimaryEngine"/> or <see cref="DictationClipEngine"/> name.</summary>
public sealed record DictationEngineChoiceDto(string Engine, bool Selectable, string? UnavailableReason);

public sealed record LocalWhisperSettingsDto(Guid? SelectedModelId, LocalWhisperEffectiveModelDto EffectiveModel, IReadOnlyList<LocalWhisperModelOption> Models);

/// <summary><see cref="Problem"/> says why Local Whisper isn't serving, or is null when it is.</summary>
public sealed record LocalWhisperEffectiveModelDto(string? Label, bool Ready, string? Problem);

public sealed class GetDictationSettingsQueryHandler(
    IDictationEngineSettingRepository settings,
    ILocalWhisperModelCatalog catalog,
    DictationVendorGate vendors) : IRequestHandler<GetDictationSettingsQuery, DictationSettingsDto>
{
    public const string NoModelProblem =
        "No Local Whisper model is selected, so dictation uses the browser built-in. Deploy ggml-base.bin under Custom Models, then select it here.";

    public async Task<DictationSettingsDto> Handle(GetDictationSettingsQuery request, CancellationToken cancellationToken)
    {
        var setting = await settings.GetOrCreateAsync(cancellationToken);

        var openAi = await vendors.WhyNotSelectableAsync(DictationTurnEngine.OpenAiWhisper, cancellationToken);
        var elevenLabs = await vendors.WhyNotSelectableAsync(DictationTurnEngine.ElevenLabsRealtime, cancellationToken);

        DictationEngineChoiceDto[] engines =
        [
            Choice(nameof(DictationPrimaryEngine.LocalWhisper), null),
            Choice(nameof(DictationPrimaryEngine.OpenAiWhisper), openAi),
            Choice(nameof(DictationPrimaryEngine.ElevenLabsRealtime), elevenLabs),
        ];
        DictationEngineChoiceDto[] pushToTalkEngines =
        [
            Choice(nameof(DictationClipEngine.LocalWhisper), null),
            Choice(nameof(DictationClipEngine.OpenAiWhisper), openAi),
            Choice(nameof(DictationClipEngine.Browser), null),
        ];

        var resolution = await catalog.ResolveSelectedAsync(setting.LocalWhisperModelId, cancellationToken);
        var models = await catalog.ListOptionsAsync(cancellationToken);

        return new DictationSettingsDto(
            setting.PrimaryEngine,
            setting.PushToTalkEngine,
            setting.State,
            setting is { State: DictationEngineState.Suspended, SuspendedEngine: { } suspended, SuspendedAtUtc: { } suspendedAt }
                ? new DictationSuspensionDto(suspended, suspendedAt, setting.SuspensionReason, BrowserInUse: true)
                : null,
            setting is { LastRevertedAtUtc: { } revertedAt, LastRevertReason: { } reason, LastRevertedFrom: { } from }
                ? new DictationRevertDto(revertedAt, reason, from)
                : null,
            engines,
            pushToTalkEngines,
            new LocalWhisperSettingsDto(setting.LocalWhisperModelId, EffectiveModel(resolution), models),
            Convert.ToBase64String(setting.RowVersion));
    }

    private static DictationEngineChoiceDto Choice(string engine, string? unavailableReason) =>
        new(engine, unavailableReason is null, unavailableReason);

    private static LocalWhisperEffectiveModelDto EffectiveModel(LocalWhisperModelResolution resolution) => resolution switch
    {
        LocalWhisperModelResolution.Ready ready => new(ready.ModelLabel, true, null),
        LocalWhisperModelResolution.Unavailable unavailable => new(
            unavailable.ModelLabel, false, $"{unavailable.Reason} Dictation uses the browser built-in until it is available again."),
        LocalWhisperModelResolution.Broken broken => new(
            broken.ModelLabel, false, $"{broken.Reason} Dictation uses the browser built-in until this is fixed."),
        _ => new(null, false, NoModelProblem),
    };
}
