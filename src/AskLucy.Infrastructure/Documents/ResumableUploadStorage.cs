using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Documents;

/// <summary>Server-filesystem <see cref="IResumableUploadStorage"/> — one growing file per session, under a root distinct from <see cref="Files.LocalFileStorage"/>'s permanent store.</summary>
public sealed class ResumableUploadStorage(IOptions<ResumableUploadStorageOptions> options) : IResumableUploadStorage
{
    private readonly string _rootPath = options.Value.RootPath;

    public async Task AppendChunkAsync(string sessionKey, Stream chunkContent, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        var fullPath = ResolveSafePath(sessionKey);
        await using var fileStream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None);
        await chunkContent.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<long> GetSizeAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(sessionKey);
        return Task.FromResult(File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L);
    }

    public Task<Stream> OpenReadAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(sessionKey);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("No staged content exists for this upload session.", sessionKey);
        }

        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(sessionKey);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveSafePath(string sessionKey)
    {
        // sessionKey is always a DocumentUploadSession's Guid id as a string; reject anything
        // containing path-traversal segments defensively regardless (mirrors LocalFileStorage).
        if (sessionKey.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(sessionKey))
        {
            throw new ArgumentException("Invalid upload session key.", nameof(sessionKey));
        }

        return Path.Combine(_rootPath, $"{sessionKey}.part");
    }
}
