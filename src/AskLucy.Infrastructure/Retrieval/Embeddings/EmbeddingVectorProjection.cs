using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>
/// Projects an embedding provider's natural-dimensionality output onto the single shared
/// <see cref="Embedding.VectorWidth"/> every provider's vectors are stored at (see
/// <c>Embedding.VectorWidth</c>'s remarks — SQL Server's native vector column is fixed-width per
/// column, but this platform supports multiple providers with different natural dimensionalities).
/// Zero-padding is safe here because a search only ever compares vectors produced by the *same*
/// provider (FR-008 already forbids mixing providers in one ranked result set) — the extra
/// trailing zero dimensions contribute nothing to cosine similarity/distance between two vectors
/// padded identically.
/// </summary>
internal static class EmbeddingVectorProjection
{
    public static float[] ProjectToSharedWidth(float[] vector)
    {
        if (vector.Length == Embedding.VectorWidth)
        {
            return vector;
        }

        if (vector.Length > Embedding.VectorWidth)
        {
            throw new InvalidOperationException(
                $"Embedding provider produced a {vector.Length}-dimension vector, exceeding the shared width of {Embedding.VectorWidth}.");
        }

        var projected = new float[Embedding.VectorWidth];
        Array.Copy(vector, projected, vector.Length);
        return projected;
    }
}
