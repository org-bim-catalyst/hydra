using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;

namespace AskLucy.Application.Tests.CustomModels;

/// <summary>An in-memory <see cref="ICustomModelRepository"/> that shares instances with the caller, so tests can read the job's writes directly.</summary>
internal sealed class FakeCustomModelRepository : ICustomModelRepository
{
    public List<CustomModel> Models { get; } = [];

    public List<CustomModelOverwrittenFile> OverwrittenFiles { get; } = [];

    public List<CustomModelProgress> ProgressWrites { get; } = [];

    public Exception? AddException { get; set; }

    public CustomModel? OverlappingActiveJob { get; set; }

    /// <summary>Runs before each progress write, e.g. to request cancellation mid-transfer.</summary>
    public Action<CustomModel>? BeforeProgressWrite { get; set; }

    public Task AddAsync(CustomModel model, CancellationToken cancellationToken = default)
    {
        if (AddException is not null)
        {
            throw AddException;
        }

        Models.Add(model);
        return Task.CompletedTask;
    }

    public Task<CustomModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(id));

    /// <summary>
    /// How many saves in a row lose a RowVersion race. Mirrors the real repository: one is retried, a
    /// second throws. A losing attempt applies to a copy, so its changes are discarded as a reload would.
    /// </summary>
    public int UpdateConflicts { get; set; }

    /// <summary>The competing write that won a lost race, applied to the stored record before the retry reloads it.</summary>
    public Action<CustomModel>? OnConflict { get; set; }

    public int UpdateApplyCount { get; private set; }

    public Task<CustomModel?> UpdateAsync(Guid id, Func<CustomModel, bool> apply, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var model = Find(id);
            if (model is null)
            {
                return Task.FromResult<CustomModel?>(null);
            }

            UpdateApplyCount++;
            if (UpdateConflicts == 0)
            {
                return Task.FromResult(apply(model) ? model : null);
            }

            if (!apply(Copy(model)))
            {
                return Task.FromResult<CustomModel?>(null);
            }

            UpdateConflicts--;
            OnConflict?.Invoke(model);
            if (attempt == 2)
            {
                throw new AskLucy.Domain.Common.ConcurrencyConflictException("The custom model was changed by another request.");
            }
        }
    }

    private static CustomModel Copy(CustomModel model) =>
        (CustomModel)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(model, null)!;

    public Task<(IReadOnlyList<CustomModel> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var live = Models.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<CustomModel>, int)>((live.Skip((page - 1) * pageSize).Take(pageSize).ToList(), live.Count));
    }

    public Task<(IReadOnlyList<CustomModelOverwrittenFile> Items, int TotalCount)> GetOverwrittenFilesAsync(
        Guid customModelId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var rows = OverwrittenFiles.Where(f => f.CustomModelId == customModelId).ToList();
        return Task.FromResult<(IReadOnlyList<CustomModelOverwrittenFile>, int)>((rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count));
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models.Any(m => !m.IsDeleted && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<CustomModel?> FindActiveJobOverlappingDestinationAsync(string destination, CancellationToken cancellationToken = default) =>
        Task.FromResult(OverlappingActiveJob);

    public Task<CustomModel?> FindAvailableForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models.FirstOrDefault(m => !m.IsDeleted
            && m.Availability == CustomModelAvailability.Available
            && string.Equals(m.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<CustomModel>> FindCompletedForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomModel>>(Models.Where(m => !m.IsDeleted
            && m.DeploymentState == CustomModelDeploymentState.Completed
            && string.Equals(m.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task UpdateProgressAsync(Guid id, CustomModelProgress progress, CancellationToken cancellationToken = default)
    {
        var model = Find(id);
        if (model is not null)
        {
            BeforeProgressWrite?.Invoke(model);
            ProgressWrites.Add(progress);
            if (model.DeploymentState == CustomModelDeploymentState.Transferring)
            {
                model.RecordProgress(progress.TransferredBytes, progress.CompletedFileCount, progress.CurrentFilePath, progress.CurrentFileBytes, progress.CurrentFileTotalBytes);
            }
        }

        return Task.CompletedTask;
    }

    public Task AddOverwrittenFileAsync(CustomModelOverwrittenFile file, CancellationToken cancellationToken = default)
    {
        OverwrittenFiles.Add(file);
        return Task.CompletedTask;
    }

    public Task<CustomModelRunSignal> GetRunSignalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var model = Find(id);
        return Task.FromResult(
            model is null || !model.IsInProgress ? CustomModelRunSignal.NoLongerInProgress
            : model.CancellationRequestedAtUtc is not null ? CustomModelRunSignal.CancellationRequested
            : CustomModelRunSignal.Continue);
    }

    public Task<IReadOnlyList<CustomModel>> ListInProgressAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomModel>>(Models.Where(m => !m.IsDeleted && m.IsInProgress).ToList());

    private CustomModel? Find(Guid id) => Models.FirstOrDefault(m => m.Id == id && !m.IsDeleted);
}

/// <summary>A repository held in memory. Every file is downloaded from exactly the bytes it lists.</summary>
internal sealed class FakeModelRepositorySource : IModelRepositorySource
{
    public const string CommitSha = "0123456789abcdef0123456789abcdef01234567";

    public ResolvedRevision? Revision { get; set; }

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<(string RepositoryId, string CommitSha, string Path)> Downloads { get; } = [];

    public List<(string RepositoryId, string Revision)> Resolutions { get; } = [];

    public Exception? DownloadException { get; set; }

    public Task<ResolvedRevision> ResolveRevisionAsync(string repositoryId, string revision, CancellationToken cancellationToken = default)
    {
        Resolutions.Add((repositoryId, revision));
        return Task.FromResult(Revision ?? new ResolvedRevision(repositoryId, CommitSha, IsPrivate: false, IsGated: false));
    }

    public Task<IReadOnlyList<ModelRepositoryFile>> ListFilesAsync(string repositoryId, string commitSha, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModelRepositoryFile>>(
            Files.Select(f => new ModelRepositoryFile(f.Key, f.Value.Length, Sha256: null, GitBlobSha1: "unused")).ToList());

    public async Task DownloadAsync(
        string repositoryId,
        string commitSha,
        ModelRepositoryFile file,
        Stream destination,
        IProgress<long> progress,
        CancellationToken cancellationToken = default)
    {
        Downloads.Add((repositoryId, commitSha, file.Path));
        if (DownloadException is not null)
        {
            throw DownloadException;
        }

        var bytes = Files[file.Path];
        await destination.WriteAsync(bytes, cancellationToken);
        progress.Report(bytes.Length);
    }
}

/// <summary>An FTP server held in memory: relative path → size.</summary>
internal sealed class FakeDeploymentFileUploader : IDeploymentFileUploader
{
    public Dictionary<string, long> RemoteFiles { get; } = new(StringComparer.Ordinal);

    public List<string> Uploads { get; } = [];

    /// <summary>How many temp files existed when each upload started.</summary>
    public List<int> StagedFileCounts { get; } = [];

    public DeploymentTargetSettings? OpenedWith { get; private set; }

    /// <summary>Size to record for the uploaded file instead of its real length.</summary>
    public long? ReportedSizeOverride { get; set; }

    public Exception? UploadException { get; set; }

    public Func<string, Task>? OnUpload { get; set; }

    public Task<IDeploymentUploadSession> OpenSessionAsync(DeploymentTargetSettings settings, CancellationToken cancellationToken = default)
    {
        OpenedWith = settings;
        return Task.FromResult<IDeploymentUploadSession>(new Session(this));
    }

    private sealed class Session(FakeDeploymentFileUploader server) : IDeploymentUploadSession
    {
        public Task<long?> GetRemoteFileSizeAsync(string remoteRelativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(server.RemoteFiles.TryGetValue(remoteRelativePath, out var size) ? size : (long?)null);

        public async Task UploadAsync(string localPath, string remoteRelativePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        {
            server.StagedFileCounts.Add(Directory.GetFiles(Path.GetDirectoryName(localPath)!).Length);
            if (server.OnUpload is not null)
            {
                await server.OnUpload(remoteRelativePath);
            }

            if (server.UploadException is not null)
            {
                throw server.UploadException;
            }

            var length = new FileInfo(localPath).Length;
            progress.Report(length);
            server.Uploads.Add(remoteRelativePath);
            server.RemoteFiles[remoteRelativePath] = server.ReportedSizeOverride ?? length;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Real folders under the system temp directory, so the job's staging can be inspected.</summary>
internal sealed class TestTempStorage : ICustomModelTempStorage, IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "asklucy-custom-model-tests", Guid.NewGuid().ToString("N"));

    public long? FreeSpace { get; set; }

    public string GetJobDirectory(Guid customModelId) => Path.Combine(Root, customModelId.ToString("N"));

    public string PrepareJobDirectory(Guid customModelId)
    {
        var path = GetJobDirectory(customModelId);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public long? GetAvailableFreeSpace(Guid customModelId) => FreeSpace;

    public void DeleteJobDirectory(Guid customModelId)
    {
        var path = GetJobDirectory(customModelId);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public IReadOnlyList<Guid> ListJobDirectories() =>
        Directory.Exists(Root)
            ? Directory.EnumerateDirectories(Root).Select(d => Guid.ParseExact(Path.GetFileName(d), "N")).ToList()
            : [];

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>Builds a model already driven to a given state through its real transitions.</summary>
internal static class CustomModelSeed
{
    public static CustomModel InState(
        CustomModelDeploymentState state,
        string name = "supertonic-3",
        string sourceUrl = "https://huggingface.co/Supertone/supertonic-3",
        string destination = "Models/supertonic-3")
    {
        if (!HuggingFaceModelSource.TryParse(sourceUrl, out var source, out _)
            || !DeploymentDestination.TryCreate(destination, AskLucy.Application.Options.CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _))
        {
            throw new ArgumentException("The seed's source or destination is invalid.");
        }

        var model = CustomModel.Create(name, source!, target!, "admin-1");
        model.AssignBackgroundJob("job-1");

        var now = DateTime.UtcNow;
        if (state != CustomModelDeploymentState.Queued)
        {
            model.StartListing(now);
        }

        if (state is CustomModelDeploymentState.Transferring or CustomModelDeploymentState.Completed)
        {
            model.BeginTransfer(new string('a', 40), 10, 1, 100);
        }

        switch (state)
        {
            case CustomModelDeploymentState.Completed:
                model.RecordProgress(10, 1, null, null, null);
                model.Complete(now);
                break;
            case CustomModelDeploymentState.Failed:
                model.Fail(CustomModelFailureKind.SourceNotFound, "Not found.", now);
                break;
            case CustomModelDeploymentState.Cancelled:
                model.RequestCancellation("admin-1", now);
                model.MarkCancelled(now);
                break;
        }

        return model;
    }
}
