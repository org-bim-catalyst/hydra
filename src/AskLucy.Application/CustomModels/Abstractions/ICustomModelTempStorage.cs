namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D6. The deployment job's own scratch folder,
/// <c>{ContentRoot}/{CustomModels:TempDirectory}/{id}</c>. Local file names are chosen by the job,
/// never taken from the repository listing.
/// </summary>
public interface ICustomModelTempStorage
{
    /// <summary>Creates the folder, emptying anything a previous run left behind, and returns its full path.</summary>
    string PrepareJobDirectory(Guid customModelId);

    /// <summary>Free bytes on the volume holding the folder, or <see langword="null"/> when it can't be determined.</summary>
    long? GetAvailableFreeSpace(Guid customModelId);

    /// <summary>Deletes the folder and everything in it. A missing folder is not an error.</summary>
    void DeleteJobDirectory(Guid customModelId);

    /// <summary>The ids of every job folder on disk. Anything under the root not named for an id is left alone.</summary>
    IReadOnlyList<Guid> ListJobDirectories();
}
