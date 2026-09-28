using AskLucy.Application.Ai.Dictation;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/078 — finds the Local Whisper model file of a Custom Models deployment. Only a Completed
/// deployment whose source URL named one <c>.bin</c> file and whose file starts with the ggml
/// magic can serve. The loose <c>App_Data/whisper-models</c> files are never read.
/// </summary>
public interface ILocalWhisperModelCatalog
{
    /// <summary>
    /// None when <paramref name="customModelId"/> is null; Unavailable when an administrator
    /// marked the deployment Unavailable or it no longer exists; Broken when its file is missing
    /// or isn't a ggml model; otherwise Ready.
    /// </summary>
    Task<LocalWhisperModelResolution> ResolveSelectedAsync(Guid? customModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A deployment's model file for "Try it" (FR-009c). Like <see cref="ResolveSelectedAsync"/>,
    /// but a deployment marked Unavailable can still be tried: judging a model before making it
    /// available is the point.
    /// </summary>
    Task<LocalWhisperModelResolution> ResolveForTrialAsync(Guid customModelId, CancellationToken cancellationToken = default);

    /// <summary>Whether a deployment can be selected as the Local Whisper model, and if not, why.</summary>
    Task<LocalWhisperModelOption> CheckSelectableAsync(Guid customModelId, CancellationToken cancellationToken = default);

    /// <summary>Every Completed deployment, each with whether it can be selected.</summary>
    Task<IReadOnlyList<LocalWhisperModelOption>> ListOptionsAsync(CancellationToken cancellationToken = default);
}
