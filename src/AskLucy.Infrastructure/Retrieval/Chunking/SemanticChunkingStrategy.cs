using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>
/// FR-001 — groups adjacent sentences by embedding-similarity boundary rather than a fixed size
/// or structural marker (research.md Decision 4): a new chunk starts whenever cosine similarity
/// between consecutive sentences drops below <see cref="SimilarityThreshold"/>, or the
/// accumulated chunk would exceed the target size. Always resolves the <c>"Local"</c> embedding
/// provider for this internal boundary-detection step — a cheap, in-process operation — rather
/// than the knowledge base's configured (possibly cloud) provider, since the sentence embeddings
/// used here are a chunking heuristic, never stored as the chunk's actual retrieval embedding.
/// </summary>
public sealed partial class SemanticChunkingStrategy(IEmbeddingServiceResolver embeddingServiceResolver) : IChunkingStrategy
{
    private const double SimilarityThreshold = 0.5;

    public ChunkingStrategy Strategy => ChunkingStrategy.Semantic;

    public async Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var sentences = SentenceBoundaryRegex()
            .Split(extractedText)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (sentences.Count == 0)
        {
            return [];
        }

        var embeddingService = embeddingServiceResolver.Resolve("Local");
        var sentenceEmbeddings = await embeddingService.EmbedBatchAsync(sentences, cancellationToken);

        var groups = new List<List<string>> { new() { sentences[0] } };
        var currentLength = sentences[0].Length;

        for (var i = 1; i < sentences.Count; i++)
        {
            var similarity = CosineSimilarity(sentenceEmbeddings[i - 1].Vector, sentenceEmbeddings[i].Vector);
            var wouldExceedTarget = currentLength + sentences[i].Length > ChunkTextHelpers.DefaultTargetCharacterSize;

            if (similarity < SimilarityThreshold || wouldExceedTarget)
            {
                groups.Add([sentences[i]]);
                currentLength = sentences[i].Length;
            }
            else
            {
                groups[^1].Add(sentences[i]);
                currentLength += sentences[i].Length;
            }
        }

        return groups
            .Select((group, index) => ChunkTextHelpers.BuildCandidate(
                string.Join(' ', group), language, pageNumber: null, section: null, heading: null, position: index))
            .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA <= 0 || magB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9])")]
    private static partial Regex SentenceBoundaryRegex();
}
