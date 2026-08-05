namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>
/// Bound from configuration (constitution §8) — file-system paths, not secrets, since this
/// provider runs entirely in-process/self-hosted (spec.md FR-009a, research.md Decision 5).
/// </summary>
public sealed class OnnxEmbeddingOptions
{
    public const string SectionName = "OnnxEmbedding";

    /// <summary>Relative to the host's content root, mirroring Whisper's model-directory convention (specs/012).</summary>
    public string ModelDirectory { get; init; } = "App_Data/embedding-models";

    public string ModelFileName { get; init; } = "model.onnx";

    public string VocabFileName { get; init; } = "vocab.txt";

    public int NaturalDimensionality { get; init; } = 384;

    public int MaxTokenCount { get; init; } = 256;
}
