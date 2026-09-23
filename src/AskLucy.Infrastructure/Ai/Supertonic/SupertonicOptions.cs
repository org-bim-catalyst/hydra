namespace AskLucy.Infrastructure.Ai.Supertonic;

/// <summary>
/// Bound from the <c>Supertonic</c> configuration section (specs/070). Every property has a
/// working default, so a host with no section at all still boots — a missing model only fails
/// the voice request that needs it, never the whole application (unlike a required option under
/// <c>ValidateOnStart</c>).
/// </summary>
public sealed class SupertonicOptions
{
    public const string SectionName = "Supertonic";

    /// <summary>Directory holding <c>onnx/</c> and <c>voice_styles/</c> exactly as published at the
    /// pinned Hugging Face revision (see <c>scripts/download-supertonic.ps1</c>). Relative paths
    /// resolve against the content root.</summary>
    public string ModelDirectory { get; init; } = "App_Data/Models/supertonic-3";

    /// <summary>The voice style used when a request names none, or one that is not installed.</summary>
    public string DefaultVoice { get; init; } = "F1";

    /// <summary>Denoising steps per chunk. The upstream default; fewer is faster and rougher.</summary>
    public int TotalSteps { get; init; } = 8;

    public double Speed { get; init; } = 1.05;

    public int IntraOpThreads { get; init; } = 2;

    /// <summary>How many chunks may be synthesized at once across the whole process. Each fp32
    /// synthesis holds a few hundred MB of working memory, so the host's memory limit sets this.</summary>
    public int MaxConcurrentSyntheses { get; init; } = 1;

    public int Mp3BitRate { get; init; } = 96;
}
