using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>specs/072 research D6. Resolves <see cref="CustomModelsOptions.TempDirectory"/> against the content root.</summary>
public sealed class CustomModelTempStorage(IHostEnvironment environment, IOptionsMonitor<CustomModelsOptions> options) : ICustomModelTempStorage
{
    public string PrepareJobDirectory(Guid customModelId)
    {
        var directory = JobDirectory(customModelId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    public long? GetAvailableFreeSpace(Guid customModelId)
    {
        var root = Path.GetPathRoot(JobDirectory(customModelId));
        return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
    }

    public void DeleteJobDirectory(Guid customModelId)
    {
        var directory = JobDirectory(customModelId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public IReadOnlyList<Guid> ListJobDirectories()
    {
        var root = RootDirectory();
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Select(directory => Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToList();
    }

    private string RootDirectory() =>
        Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.CurrentValue.TempDirectory));

    private string JobDirectory(Guid customModelId) =>
        Path.Combine(RootDirectory(), customModelId.ToString("N"));
}
