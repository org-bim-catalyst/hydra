namespace AskLucy.Infrastructure.Ai;

public sealed class WhisperOptions
{
    public const string SectionName = "Whisper";

    /// <summary>Where the ggml model file is cached after its one-time download.</summary>
    public string ModelDirectory { get; init; } = "App_Data/whisper-models";

    /// <summary>One of Whisper.net.Ggml.GgmlType's names (e.g. "BaseEn", "SmallEn").</summary>
    public string ModelSize { get; init; } = "BaseEn";
}
