namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>A revision pinned to its commit. <see cref="RepositoryId"/> carries the source's canonical casing (research D3 step 4).</summary>
public sealed record ResolvedRevision(string RepositoryId, string CommitSha, bool IsPrivate, bool IsGated);

/// <summary>One file in a repository listing. <see cref="Sha256"/> is set for LFS files; <see cref="GitBlobSha1"/> always is.</summary>
public sealed record ModelRepositoryFile(string Path, long Size, string? Sha256, string GitBlobSha1);

/// <summary>
/// specs/072 research D3/D4. A model repository host (Hugging Face today). Implementations build
/// every URL themselves from the repository id and commit; the URL an admin typed is never fetched.
/// Every failure is a <see cref="ModelRepositorySourceException"/>.
/// </summary>
public interface IModelRepositorySource
{
    Task<ResolvedRevision> ResolveRevisionAsync(string repositoryId, string revision, CancellationToken cancellationToken = default);

    /// <summary>Every file (not directory) at the commit, following pagination.</summary>
    Task<IReadOnlyList<ModelRepositoryFile>> ListFilesAsync(string repositoryId, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>Streams the file at the commit into <paramref name="destination"/>, reporting its downloaded bytes, then verifies its hash.</summary>
    Task DownloadAsync(
        string repositoryId,
        string commitSha,
        ModelRepositoryFile file,
        Stream destination,
        IProgress<long> progress,
        CancellationToken cancellationToken = default);
}
