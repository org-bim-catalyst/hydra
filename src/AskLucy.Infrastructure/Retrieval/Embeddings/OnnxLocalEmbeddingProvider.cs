using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>
/// The local/self-hosted <see cref="IEmbeddingService"/> (spec.md FR-009a, research.md
/// Decision 5) — a BERT-family sentence-embedding model (e.g. all-MiniLM-L6-v2) run entirely
/// in-process via ONNX Runtime, no network call per request, mirroring the exact precedent
/// specs/015's plan.md cites for OCR: "a self-hosted OCR engine (Tesseract, mirroring the
/// existing self-hosted Whisper.net STT precedent)" — <see cref="WhisperLocalTranscriptionProvider"/>
/// (specs/012) is the same in-process, no-network-call shape this provider follows. This is what
/// literally satisfies "content never leaves the platform's environment" (FR-009a).
///
/// <para><b>Deployment prerequisite</b> (not a code gap — same category as Tesseract's trained-
/// data files, specs/015): the model's <c>model.onnx</c> (exported with <c>last_hidden_state</c>
/// output, standard sentence-transformers ONNX export shape) and matching <c>vocab.txt</c> must be
/// present under <see cref="OnnxEmbeddingOptions.ModelDirectory"/> before this provider can serve a
/// request — sourcing/licensing a specific model file is an ops/deployment decision, not something
/// this code should fetch from a hardcoded URL at runtime.</para>
/// </summary>
public sealed class OnnxLocalEmbeddingProvider : IEmbeddingService, IDisposable
{
    private readonly OnnxEmbeddingOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;

    public OnnxLocalEmbeddingProvider(IOptions<OnnxEmbeddingOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public string ProviderKey => "Local";

    public int Dimensionality => AskLucy.Domain.Retrieval.Embedding.VectorWidth;

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var (session, tokenizer) = await GetModelAsync(cancellationToken);
        var results = new List<EmbeddingResult>(texts.Count);

        // One inference call per text (not batched into a single padded tensor) — the simplest
        // correct approach; batching same-length-padded sequences is a follow-up optimization
        // (FR-050 is satisfied at the orchestrator level by generating embeddings for many chunks
        // without blocking the caller, not necessarily via a single fused ONNX batch call).
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = tokenizer.EncodeToIds(text, _options.MaxTokenCount, addSpecialTokens: true, out _, out _);
            var inputIds = ids.Select(i => (long)i).ToArray();
            var attentionMask = inputIds.Select(_ => 1L).ToArray();
            var tokenTypeIds = new long[inputIds.Length];

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, inputIds.Length])),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, [1, inputIds.Length])),
                NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, [1, inputIds.Length])),
            };

            using var output = session.Run(inputs);
            var lastHiddenState = output.First(v => v.Name == "last_hidden_state").AsTensor<float>();

            var pooled = MeanPool(lastHiddenState, attentionMask);
            var normalized = Normalize(pooled);
            results.Add(new EmbeddingResult(EmbeddingVectorProjection.ProjectToSharedWidth(normalized), AskLucy.Domain.Retrieval.Embedding.VectorWidth));
        }

        return results;
    }

    /// <summary>Mean-pools token embeddings over the sequence dimension, weighted by the attention mask (the standard sentence-transformers pooling strategy for BERT-family last_hidden_state output).</summary>
    private static float[] MeanPool(Tensor<float> lastHiddenState, long[] attentionMask)
    {
        var hiddenSize = lastHiddenState.Dimensions[2];
        var pooled = new float[hiddenSize];
        var totalWeight = 0f;

        for (var tokenIndex = 0; tokenIndex < attentionMask.Length; tokenIndex++)
        {
            if (attentionMask[tokenIndex] == 0)
            {
                continue;
            }

            totalWeight++;
            for (var d = 0; d < hiddenSize; d++)
            {
                pooled[d] += lastHiddenState[0, tokenIndex, d];
            }
        }

        if (totalWeight > 0)
        {
            for (var d = 0; d < hiddenSize; d++)
            {
                pooled[d] /= totalWeight;
            }
        }

        return pooled;
    }

    private static float[] Normalize(float[] vector)
    {
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude <= 0f)
        {
            return vector;
        }

        return vector.Select(v => v / magnitude).ToArray();
    }

    private async Task<(InferenceSession Session, BertTokenizer Tokenizer)> GetModelAsync(CancellationToken cancellationToken)
    {
        if (_session is not null && _tokenizer is not null)
        {
            return (_session, _tokenizer);
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && _tokenizer is not null)
            {
                return (_session, _tokenizer);
            }

            var directory = Path.Combine(_environment.ContentRootPath, _options.ModelDirectory);
            var modelPath = Path.Combine(directory, _options.ModelFileName);
            var vocabPath = Path.Combine(directory, _options.VocabFileName);

            if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            {
                throw new AiProviderUnavailableException(
                    $"The local embedding model is not installed. Expected '{modelPath}' and '{vocabPath}' — " +
                    "place a sentence-embedding ONNX model (exported with a 'last_hidden_state' output) and its " +
                    "vocab.txt at this path before using a data-residency-restricted knowledge base.");
            }

            _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions());
            _session = new InferenceSession(modelPath);
            return (_session, _tokenizer);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initLock.Dispose();
    }
}
