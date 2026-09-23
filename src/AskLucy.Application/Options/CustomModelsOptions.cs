using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4). Limits and timings for server-side custom model deployments (specs/072 data-model.md). Every key has a default, so a missing section never fails host startup.</summary>
public sealed class CustomModelsOptions
{
    public const string SectionName = "CustomModels";

    /// <summary>Used when <see cref="AllowedDestinationPrefixes"/> is empty. Kept separate from the property because the configuration binder appends a bound array onto a non-empty default instead of replacing it.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedDestinationPrefixes = ["Models", "App_Data/Models"];

    /// <summary>FR-008. A deployment whose listed files total more than this fails during Listing. Defaults to 20 GB.</summary>
    [Range(1, long.MaxValue)]
    public long MaxDeploymentBytes { get; init; } = 21_474_836_480;

    /// <summary>FR-005a (research D5). A destination must sit strictly below one of these. Empty means <see cref="DefaultAllowedDestinationPrefixes"/>; read it through <see cref="GetAllowedDestinationPrefixes"/>.</summary>
    public string[] AllowedDestinationPrefixes { get; init; } = [];

    /// <summary>Resolved against the content root. Each deployment gets its own <c>{id}</c> subfolder.</summary>
    public string TempDirectory { get; init; } = "App_Data/Temp/custom-models";

    /// <summary>FR-021. Persisted progress is at most this old after a page reload.</summary>
    [Range(1, 60)]
    public int ProgressPersistIntervalSeconds { get; init; } = 2;

    /// <summary>SC-004. Progress events are pushed at most this often per job.</summary>
    [Range(100, 10_000)]
    public int ProgressPushIntervalMilliseconds { get; init; } = 500;

    /// <summary>Research D6. A download that receives no bytes for this long fails as stalled.</summary>
    [Range(5, 600)]
    public int StallTimeoutSeconds { get; init; } = 60;

    public IReadOnlyList<string> GetAllowedDestinationPrefixes() =>
        AllowedDestinationPrefixes is { Length: > 0 }
            ? AllowedDestinationPrefixes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : DefaultAllowedDestinationPrefixes;
}
