using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using Microsoft.Extensions.Hosting;

namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>
/// specs/078 — resolves the Local Whisper model file of a Custom Models deployment:
/// <c>&lt;content root&gt;/&lt;Destination&gt;/&lt;SourceFilePath&gt;</c>, which must stay inside the
/// deployment's folder and start with the ggml magic. Scoped: it reads through the request's
/// <see cref="ICustomModelRepository"/>, and nothing runs it alongside other work on that request.
/// </summary>
public sealed class LocalWhisperModelCatalog(ICustomModelRepository repository, IHostEnvironment environment) : ILocalWhisperModelCatalog
{
    /// <summary>whisper.cpp writes the magic 0x67676d6c little-endian: the bytes "lmgg".</summary>
    private static readonly byte[] GgmlMagic = "lmgg"u8.ToArray();

    public async Task<LocalWhisperModelResolution> ResolveSelectedAsync(Guid? customModelId, CancellationToken cancellationToken = default)
    {
        if (customModelId is not { } id)
        {
            return LocalWhisperModelResolution.None.Instance;
        }

        var model = await repository.GetByIdAsync(id, cancellationToken);
        if (model is null || model.DeploymentState != CustomModelDeploymentState.Completed)
        {
            return new LocalWhisperModelResolution.Unavailable("The selected model is no longer deployed under Custom Models.", model?.Name);
        }

        if (model.Availability != CustomModelAvailability.Available)
        {
            return new LocalWhisperModelResolution.Unavailable("The selected model is marked Unavailable under Custom Models.", Label(model));
        }

        var problem = CheckFile(model, out var path);
        return problem is null
            ? new LocalWhisperModelResolution.Ready(path!, Label(model), Path.GetFileName(path!))
            : new LocalWhisperModelResolution.Broken(problem, Label(model));
    }

    public async Task<LocalWhisperModelResolution> ResolveForTrialAsync(Guid customModelId, CancellationToken cancellationToken = default)
    {
        var model = await repository.GetByIdAsync(customModelId, cancellationToken);
        if (model is null || model.DeploymentState != CustomModelDeploymentState.Completed)
        {
            return new LocalWhisperModelResolution.Unavailable("Only a completed deployment can be tried.", model?.Name);
        }

        var problem = CheckFile(model, out var path);
        return problem is null
            ? new LocalWhisperModelResolution.Ready(path!, Label(model), Path.GetFileName(path!))
            : new LocalWhisperModelResolution.Broken(problem, Label(model));
    }

    public async Task<LocalWhisperModelOption> CheckSelectableAsync(Guid customModelId, CancellationToken cancellationToken = default)
    {
        var model = await repository.GetByIdAsync(customModelId, cancellationToken);
        return model is null
            ? new LocalWhisperModelOption(customModelId, customModelId.ToString(), false, "The deployment no longer exists.")
            : ToOption(model);
    }

    public async Task<IReadOnlyList<LocalWhisperModelOption>> ListOptionsAsync(CancellationToken cancellationToken = default)
    {
        var completed = await repository.ListCompletedAsync(cancellationToken);
        return completed.Select(ToOption).ToList();
    }

    private LocalWhisperModelOption ToOption(CustomModel model)
    {
        var reason = model.DeploymentState != CustomModelDeploymentState.Completed
            ? "Only a completed deployment can be selected."
            : CheckFile(model, out _);
        return new LocalWhisperModelOption(model.Id, Label(model), reason is null, reason);
    }

    /// <summary>Null when the deployment holds a readable ggml file; otherwise why not.</summary>
    private string? CheckFile(CustomModel model, out string? path)
    {
        path = null;
        if (string.IsNullOrEmpty(model.SourceFilePath))
        {
            return "Deploy the model from a URL that names its .bin file.";
        }

        var root = Path.GetFullPath(environment.ContentRootPath);
        var folder = Path.GetFullPath(Path.Combine(root, model.Destination));
        var file = Path.GetFullPath(Path.Combine(folder, model.SourceFilePath));
        var folderWithSeparator = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        if (!file.StartsWith(folderWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return "The model file is outside its deployment folder.";
        }

        if (!File.Exists(file))
        {
            return "The model file is missing from its deployment folder.";
        }

        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) != magic.Length || !magic.SequenceEqual(GgmlMagic))
            {
                return "The file isn't a Whisper (ggml) model.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The model file can't be read.";
        }

        path = file;
        return null;
    }

    private static string Label(CustomModel model) =>
        string.IsNullOrEmpty(model.SourceFilePath) ? model.Name : $"{model.Name} ({Path.GetFileName(model.SourceFilePath)})";
}
