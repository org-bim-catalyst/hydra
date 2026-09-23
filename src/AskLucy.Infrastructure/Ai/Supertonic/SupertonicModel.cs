// Inference loop ported from Supertone's Supertonic C# reference implementation (Helper.cs):
// https://github.com/supertone-inc/supertonic — MIT License, Copyright (c) 2026 Supertone Inc.
// The full notice travels with SupertonicText.cs; the model weights themselves are licensed
// separately under OpenRAIL-M (see docs/THIRD_PARTY_NOTICES.md).

using System.Collections.Concurrent;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AskLucy.Infrastructure.Ai.Supertonic;

/// <summary>
/// The in-process Supertonic 3 model (specs/070): four ONNX sessions — duration predictor, text
/// encoder, vector estimator (flow-matching denoiser) and vocoder — loaded lazily on first use and
/// held for the life of the process, the same shape as <c>OnnxLocalEmbeddingProvider</c>.
///
/// <para><b>Deployment prerequisite:</b> the pinned model files must be present under
/// <see cref="SupertonicOptions.ModelDirectory"/> (<c>scripts/download-supertonic.ps1</c>). A
/// missing file surfaces as <see cref="AiProviderUnavailableException"/> on the request that needed
/// it, so the voice router fails over rather than the host refusing to start.</para>
/// </summary>
internal sealed partial class SupertonicModel : IDisposable
{
    private static readonly string[] OnnxFiles =
        ["duration_predictor.onnx", "text_encoder.onnx", "vector_estimator.onnx", "vocoder.onnx", "tts.json", "unicode_indexer.json"];

    private readonly SupertonicOptions _options;
    private readonly ILogger<SupertonicModel> _logger;
    private readonly string _modelDirectory;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _synthesisGate;
    private readonly ConcurrentDictionary<string, VoiceStyle> _voiceStyles = new(StringComparer.OrdinalIgnoreCase);
    private LoadedModel? _model;

    public SupertonicModel(IOptions<SupertonicOptions> options, IHostEnvironment environment, ILogger<SupertonicModel> logger)
    {
        _options = options.Value;
        _logger = logger;
        _modelDirectory = Path.IsPathRooted(_options.ModelDirectory)
            ? _options.ModelDirectory
            : Path.Combine(environment.ContentRootPath, _options.ModelDirectory);
        _synthesisGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentSyntheses));
    }

    private string VoiceStyleDirectory => Path.Combine(_modelDirectory, "voice_styles");

    /// <summary>The voice style ids installed on this server (file names under <c>voice_styles/</c>),
    /// sorted. Reading the directory does not load the ONNX sessions.</summary>
    public IReadOnlyList<string> ListInstalledVoices()
    {
        if (!Directory.Exists(VoiceStyleDirectory))
        {
            LogModelMissing(_logger, VoiceStyleDirectory);
            throw new AiProviderUnavailableException("The Supertonic voice model is not installed on this server.");
        }

        return [.. Directory.EnumerateFiles(VoiceStyleDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Loads the model (first call only) and the named voice style. The id must be one
    /// <see cref="ListInstalledVoices"/> returned — it is matched against that list rather than
    /// joined into a path, so a request can never reach a file outside <c>voice_styles/</c>.</summary>
    public async Task<SupertonicVoice> LoadVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(cancellationToken);
        var installed = ListInstalledVoices().FirstOrDefault(v => string.Equals(v, voiceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new AiProviderRequestInvalidException($"The Supertonic voice '{voiceId}' is not installed.");

        var style = _voiceStyles.GetOrAdd(installed, id => ReadVoiceStyle(Path.Combine(VoiceStyleDirectory, id + ".json")));
        return new SupertonicVoice(installed, model.SampleRate, style);
    }

    /// <summary>Synthesizes one chunk (already sized by <see cref="SupertonicText.Chunk"/>) to mono
    /// float PCM at <see cref="SupertonicVoice.SampleRate"/>. Waits its turn behind
    /// <see cref="SupertonicOptions.MaxConcurrentSyntheses"/>, then runs on the thread pool.</summary>
    public async Task<float[]> SynthesizeAsync(string chunk, string language, SupertonicVoice voice, float speed, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(cancellationToken);
        await _synthesisGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Infer(model, chunk, language, voice.Style, speed, cancellationToken), cancellationToken);
        }
        catch (OnnxRuntimeException ex)
        {
            LogInferenceFailed(_logger, ex);
            throw new AiProviderUnavailableException("The Supertonic voice model failed to synthesize this text.", ex);
        }
        finally
        {
            _synthesisGate.Release();
        }
    }

    private float[] Infer(LoadedModel model, string chunk, string language, VoiceStyle style, float speed, CancellationToken cancellationToken)
    {
        var tokenIds = SupertonicText.ToTokenIds(SupertonicText.Preprocess(chunk, language), model.Indexer);
        var length = tokenIds.Length;

        var textIds = new DenseTensor<long>(tokenIds, [1, length]);
        var textMask = new DenseTensor<float>(Ones(length), [1, 1, length]);
        var styleTtl = new DenseTensor<float>(style.Ttl, style.TtlDimensions);
        var styleDp = new DenseTensor<float>(style.Dp, style.DpDimensions);

        float duration;
        using (var durationOutputs = model.DurationPredictor.Run(
        [
            NamedOnnxValue.CreateFromTensor("text_ids", textIds),
            NamedOnnxValue.CreateFromTensor("style_dp", styleDp),
            NamedOnnxValue.CreateFromTensor("text_mask", textMask),
        ]))
        {
            duration = durationOutputs.First(o => o.Name == "duration").AsTensor<float>().GetValue(0) / speed;
        }

        using var encoderOutputs = model.TextEncoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("text_ids", textIds),
            NamedOnnxValue.CreateFromTensor("style_ttl", styleTtl),
            NamedOnnxValue.CreateFromTensor("text_mask", textMask),
        ]);
        var textEmbedding = encoderOutputs.First(o => o.Name == "text_emb").AsTensor<float>();

        // Upstream sizes the latent with float arithmetic but its mask with integer arithmetic,
        // which disagree by one frame when duration * sampleRate lands just past a chunk
        // boundary. Sizing both from the same integer keeps the shapes consistent.
        var wavLength = (long)(duration * model.SampleRate);
        var latentChunk = model.BaseChunkSize * model.ChunkCompressFactor;
        var latentLength = (int)Math.Max(1, (wavLength + latentChunk - 1) / latentChunk);
        var latentChannels = model.LatentDim * model.ChunkCompressFactor;
        int[] latentShape = [1, latentChannels, latentLength];

        var latent = GaussianNoise(latentChannels * latentLength);
        var latentMask = new DenseTensor<float>(Ones(latentLength), [1, 1, latentLength]);
        var totalSteps = Math.Max(1, _options.TotalSteps);
        var totalStepTensor = new DenseTensor<float>(new float[] { totalSteps }, [1]);

        for (var step = 0; step < totalSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stepOutputs = model.VectorEstimator.Run(
            [
                NamedOnnxValue.CreateFromTensor("noisy_latent", new DenseTensor<float>(latent, latentShape)),
                NamedOnnxValue.CreateFromTensor("text_emb", textEmbedding),
                NamedOnnxValue.CreateFromTensor("style_ttl", styleTtl),
                NamedOnnxValue.CreateFromTensor("text_mask", textMask),
                NamedOnnxValue.CreateFromTensor("latent_mask", latentMask),
                NamedOnnxValue.CreateFromTensor("total_step", totalStepTensor),
                NamedOnnxValue.CreateFromTensor("current_step", new DenseTensor<float>(new float[] { step }, [1])),
            ]);
            latent = stepOutputs.First(o => o.Name == "denoised_latent").AsTensor<float>().ToArray();
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var vocoderOutputs = model.Vocoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("latent", new DenseTensor<float>(latent, latentShape)),
        ]);
        var wav = vocoderOutputs.First(o => o.Name == "wav_tts").AsTensor<float>().ToArray();

        // The vocoder pads to a whole latent frame; the predicted duration is the speech itself.
        return wav.Length > wavLength ? wav[..(int)wavLength] : wav;
    }

    private async Task<LoadedModel> GetModelAsync(CancellationToken cancellationToken)
    {
        if (_model is not null)
        {
            return _model;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model is not null)
            {
                return _model;
            }

            var onnxDirectory = Path.Combine(_modelDirectory, "onnx");
            var missing = OnnxFiles.Where(f => !File.Exists(Path.Combine(onnxDirectory, f))).ToList();
            if (missing.Count > 0)
            {
                LogModelMissing(_logger, string.Join(", ", missing.Select(f => Path.Combine(onnxDirectory, f))));
                throw new AiProviderUnavailableException("The Supertonic voice model is not installed on this server.");
            }

            _model = await Task.Run(() => LoadModel(onnxDirectory), cancellationToken);
            LogModelLoaded(_logger, _model.SampleRate);
            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private LoadedModel LoadModel(string onnxDirectory)
    {
        // The memory arena grows to the largest request seen and never shrinks — on a shared
        // host that is a permanent few-hundred-MB high-water mark. Without it each run allocates
        // and frees its own buffers, which measured no slower for these model sizes.
        using var sessionOptions = new SessionOptions
        {
            EnableCpuMemArena = false,
            IntraOpNumThreads = Math.Max(1, _options.IntraOpThreads),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(onnxDirectory, "tts.json")));
        var autoencoder = config.RootElement.GetProperty("ae");
        var textToLatent = config.RootElement.GetProperty("ttl");
        var indexer = JsonSerializer.Deserialize<long[]>(File.ReadAllText(Path.Combine(onnxDirectory, "unicode_indexer.json")))
            ?? throw new AiProviderUnavailableException("The Supertonic voice model's text indexer is unreadable.");

        InferenceSession Load(string file) => new(Path.Combine(onnxDirectory, file), sessionOptions);

        return new LoadedModel(
            Load("duration_predictor.onnx"),
            Load("text_encoder.onnx"),
            Load("vector_estimator.onnx"),
            Load("vocoder.onnx"),
            indexer,
            autoencoder.GetProperty("sample_rate").GetInt32(),
            autoencoder.GetProperty("base_chunk_size").GetInt32(),
            textToLatent.GetProperty("chunk_compress_factor").GetInt32(),
            textToLatent.GetProperty("latent_dim").GetInt32());
    }

    private static VoiceStyle ReadVoiceStyle(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var (ttl, ttlDimensions) = ReadStyleTensor(document.RootElement.GetProperty("style_ttl"));
        var (dp, dpDimensions) = ReadStyleTensor(document.RootElement.GetProperty("style_dp"));
        return new VoiceStyle(ttl, ttlDimensions, dp, dpDimensions);
    }

    private static (float[] Data, int[] Dimensions) ReadStyleTensor(JsonElement element)
    {
        var dimensions = element.GetProperty("dims").EnumerateArray().Select(d => d.GetInt32()).ToArray();
        var data = new List<float>(dimensions.Aggregate(1, (product, d) => product * d));
        foreach (var batch in element.GetProperty("data").EnumerateArray())
        {
            foreach (var row in batch.EnumerateArray())
            {
                data.AddRange(row.EnumerateArray().Select(v => v.GetSingle()));
            }
        }

        return ([.. data], dimensions);
    }

    private static float[] Ones(int length)
    {
        var values = new float[length];
        Array.Fill(values, 1f);
        return values;
    }

    /// <summary>Standard normal samples (Box–Muller), the flow-matching starting point.</summary>
    private static float[] GaussianNoise(int length)
    {
        var values = new float[length];
        for (var i = 0; i < length; i++)
        {
            var u1 = 1.0 - Random.Shared.NextDouble();
            var u2 = 1.0 - Random.Shared.NextDouble();
            values[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        return values;
    }

    public void Dispose()
    {
        _model?.Dispose();
        _initLock.Dispose();
        _synthesisGate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Supertonic model files are missing: {Paths}")]
    private static partial void LogModelMissing(ILogger logger, string paths);

    [LoggerMessage(Level = LogLevel.Information, Message = "Supertonic model loaded ({SampleRate} Hz)")]
    private static partial void LogModelLoaded(ILogger logger, int sampleRate);

    [LoggerMessage(Level = LogLevel.Error, Message = "Supertonic inference failed")]
    private static partial void LogInferenceFailed(ILogger logger, Exception exception);

    internal sealed record VoiceStyle(float[] Ttl, int[] TtlDimensions, float[] Dp, int[] DpDimensions);

    private sealed record LoadedModel(
        InferenceSession DurationPredictor,
        InferenceSession TextEncoder,
        InferenceSession VectorEstimator,
        InferenceSession Vocoder,
        long[] Indexer,
        int SampleRate,
        int BaseChunkSize,
        int ChunkCompressFactor,
        int LatentDim) : IDisposable
    {
        public void Dispose()
        {
            DurationPredictor.Dispose();
            TextEncoder.Dispose();
            VectorEstimator.Dispose();
            Vocoder.Dispose();
        }
    }
}

/// <summary>A loaded voice style, ready to pass to <see cref="SupertonicModel.SynthesizeAsync"/>.</summary>
internal sealed record SupertonicVoice(string Id, int SampleRate, SupertonicModel.VoiceStyle Style);
