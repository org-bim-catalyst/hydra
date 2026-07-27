using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Files;

/// <summary>
/// Server-filesystem <see cref="IFileStorage"/> implementation (docs/ARCHITECTURE.md &#167;17).
/// Files are stored outside the web root under a random, non-guessable name — never the
/// caller-supplied original file name (constitution &#167;8: "generate random file names").
/// </summary>
public sealed class LocalFileStorage(IOptions<LocalFileStorageOptions> options) : IFileStorage
{
    private readonly string _rootPath = options.Value.RootPath;

    public async Task<string> SaveAsync(Stream content, string fileNameHint, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        var extension = Path.GetExtension(fileNameHint);
        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(_rootPath, storedFileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storedFileName;
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(storedFileName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested file does not exist.", storedFileName);
        }

        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    private string ResolveSafePath(string storedFileName)
    {
        // storedFileName is always a GUID-based name minted by SaveAsync above; reject
        // anything containing path-traversal segments defensively regardless.
        if (storedFileName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(storedFileName))
        {
            throw new ArgumentException("Invalid stored file name.", nameof(storedFileName));
        }

        return Path.Combine(_rootPath, storedFileName);
    }
}
