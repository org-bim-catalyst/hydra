namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// One connection to the deployment target. Every path is relative to the deployment root; only the
/// implementation joins it to <see cref="DeploymentTargetSettings.RootPath"/> (FR-018). Every
/// failure is a <see cref="DeploymentTargetException"/>.
/// </summary>
public interface IDeploymentUploadSession : IAsyncDisposable
{
    /// <summary>The size of an existing remote file, or <see langword="null"/> when there is none (FR-010a).</summary>
    Task<long?> GetRemoteFileSizeAsync(string remoteRelativePath, CancellationToken cancellationToken = default);

    /// <summary>Uploads, overwriting any existing file and creating missing directories. Reports this file's uploaded bytes.</summary>
    Task UploadAsync(string localPath, string remoteRelativePath, IProgress<long> progress, CancellationToken cancellationToken = default);
}
