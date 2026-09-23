// Inference loop ported from Supertone's Supertonic C# reference implementation (Helper.cs):
// https://github.com/supertone-inc/supertonic — MIT License, Copyright (c) 2026 Supertone Inc.
// The full notice travels with SupertonicText.cs; the model weights themselves are licensed
// separately under OpenRAIL-M (see docs/THIRD_PARTY_NOTICES.md).

using System.Collections.Concurrent;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
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
/// <para><b>Where the files come from</b> (specs/072 research D9): each voice request asks
/// <see cref="IHostedModelLocator"/> once. With no completed custom model record for
/// <see cref="RepositoryId"/> the pinned files under <see cref="SupertonicOptions.ModelDirectory"/>
/// are used (<c>scripts/download-supertonic.ps1</c>); with an Available one, its deployed folder
/// under the content root. When that folder changes the old sessions are disposed and the new ones
/// loaded; when the record is Unavailable the sessions are disposed and the request fails. A missing
/// file or an Unavailable model surfaces as <see cref="AiProviderUnavailableException"/> on the
/// request that needed it, so the voice router fails over rather than the host refusing to start.</para>
/// </summary>
internal sealed partial class SupertonicModel : IDisposable
{
    /// <summary>The Hugging Face repository a custom model deploys this engine's files from.</summary>
    public const string RepositoryId = "Supertone/supertonic-3";

    private const string NotInstalledMessage = "The Supertonic voice model is not installed on this server.";
    private const string DeployedFilesMissingMessage = "The Supertonic model files were not found on this server.";
    private const string MarkedUnavailableMessage = "The Supertonic model is marked unavailable in Custom Models.";

    private static readonly string[] OnnxFiles =
        ["duration_predictor.onnx", "text_encoder.onnx", "vector_estimator.onnx", "vocoder.onnx", "tts.json", "unicode_indexer.json"];

    private readonly SupertonicOptions _options;
    private readonly IHostedModelLocator _locator;
    private readonly ILogger<SupertonicModel> _logger;
    private readonly string _contentRoot;
    private readonly string _configuredDirectory;
    private readonly int _synthesisSlots;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _synthesisGate;
    private readonly ConcurrentDictionary<string, VoiceStyle> _voiceStyles = new(StringComparer.OrdinalIgnoreCase);
    private volatile LoadedModel? _model;

    public SupertonicModel(IOptions<SupertonicOptions> options, IHostEnvironment environment, IHostedModelLocator locator, ILogger<SupertonicModel> logger)
    {
        _options = options.Value;
        _locator = locator;
        _logger = logger;
        _contentRoot = Path.GetFullPath(environment.ContentRootPath);
        _configuredDirectory = Path.GetFullPath(Path.Combine(_contentRoot, _options.ModelDirectory));
        _synthesisSlots = Math.Max(1, _options.MaxConcurrentSyntheses);
        _synthesisGate = new SemaphoreSlim(_synthesisSlots);
    }

    /// <summary>The folder whose ONNX sessions are loaded, or null when none are.</summary>
    internal string? LoadedDirectory => _model?.Directory;

    /// <summary>Resolves which folder this request uses (one locator call) and lists the voice style
    /// ids installed there (file names under <c>voice_styles/</c>), sorted. Does not load the ONNX
    /// sessions, but disposes them when the model has been made Unavailable.</summary>
    public async Task<SupertonicInstall> ResolveInstallAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _locator.ResolveAsync(RepositoryId, cancellationToken);
        switch (resolution)
        {
            case HostedModelResolution.Unavailable:
                await RetireAsync(cancellationToken);
                LogMarkedUnavailable(_logger, RepositoryId);
                throw new AiProviderUnavailableException(MarkedUnavailableMessage);

            case HostedModelResolution.Available available:
                var deployed = Path.GetFullPath(Path.Combine(_contentRoot, available.RelativeDirectory));
                if (!IsUnder(deployed, _contentRoot))
                {
                    LogModelMissing(_logger, deployed);
                    throw new AiProviderUnavailableException(DeployedFilesMissingMessage);
                }

                return new SupertonicInstall(deployed, true, ListVoices(deployed, DeployedFilesMissingMessage));

            default:
                return new SupertonicInstall(_configuredDirectory, false, ListVoices(_configuredDirectory, NotInstalledMessage));
        }
    }

    /// <summary>specs/072 FR-037 — the message a voice request would fail with right now, or null.
    /// With no custom model record the configured install is today's behaviour (FR-039) and is not
    /// second-guessed here; preview still reports a missing one. Never loads, unloads or logs.</summary>
    public async Task<string?> FindModelProblemAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _locator.ResolveAsync(RepositoryId, cancellationToken);
        if (resolution is HostedModelResolution.Unavailable)
        {
            return MarkedUnavailableMessage;
        }

        if (resolution is not HostedModelResolution.Available available)
        {
            return null;
        }

        var deployed = Path.GetFullPath(Path.Combine(_contentRoot, available.RelativeDirectory));
        var complete = IsUnder(deployed, _contentRoot)
            && Directory.Exists(Path.Combine(deployed, "voice_styles"))
            && OnnxFiles.All(f => File.Exists(Path.Combine(deployed, "onnx", f)));
        return complete ? null : DeployedFilesMissingMessage;
    }

    /// <summary>Loads the install's ONNX sessions (unless already loaded) and the named voice style.
    /// The id must be one the install lists — it is matched against that list rather than joined
    /// into a path, so a request can never reach a file outside <c>voice_styles/</c>.</summary>
    public async Task<SupertonicVoice> LoadVoiceAsync(SupertonicInstall install, string voiceId, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(install, cancellationToken);
        var installed = install.Voices.FirstOrDefault(v => string.Equals(v, voiceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new AiProviderRequestInvalidException($"The Supertonic voice '{voiceId}' is not installed.");

        var path = Path.Combine(install.Directory, "voice_styles", installed + ".json");
        var style = _voiceStyles.GetOrAdd(path, ReadVoiceStyle);
        return new SupertonicVoice(installed, model.SampleRate, style, model);
    }

    /// <summary>Synthesizes one chunk (already sized by <see cref="SupertonicText.Chunk"/>) to mono
    /// float PCM at <see cref="SupertonicVoice.SampleRate"/>. Waits its turn behind
    /// <see cref="SupertonicOptions.MaxConcurrentSyntheses"/>, then runs on the thread pool.</summary>
    public async Task<float[]> SynthesizeAsync(string chunk, string language, SupertonicVoice voice, float speed, CancellationToken cancellationToken = default)
    {
        await _synthesisGate.WaitAsync(cancellationToken);
        try
        {
            // Sessions are only disposed while every gate slot is held, so once through the gate
            // this check stays true until the inference below has finished.
            if (voice.Model.IsDisposed)
            {
                LogModelSwitched(_logger);
                throw new AiProviderUnavailableException("The Supertonic model was switched while speaking.");
            }

            return await Task.Run(() => Infer(voice.Model, chunk, language, voice.Style, speed, cancellationToken), cancellationToken);
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

    private IReadOnlyList<string> ListVoices(string directory, string missingMessage)
    {
        var voiceStyles = Path.Combine(directory, "voice_styles");
        if (!Directory.Exists(voiceStyles))
        {
            LogModelMissing(_logger, voiceStyles);
            throw new AiProviderUnavailableException(missingMessage);
        }

        return [.. Directory.EnumerateFiles(voiceStyles, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<LoadedModel> GetModelAsync(SupertonicInstall install, CancellationToken cancellationToken)
    {
        var current = _model;
        if (current is not null && PathEquals(current.Directory, install.Directory))
        {
            return current;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            current = _model;
            if (current is not null && PathEquals(current.Directory, install.Directory))
            {
                return current;
            }

            // A different folder is now in use; free the old sessions (~450 MB) before anything else.
            await RetireLockedAsync();

            var onnxDirectory = Path.Combine(install.Directory, "onnx");
            var missing = OnnxFiles.Where(f => !File.Exists(Path.Combine(onnxDirectory, f))).ToList();
            if (missing.Count > 0)
            {
                LogModelMissing(_logger, string.Join(", ", missing.Select(f => Path.Combine(onnxDirectory, f))));
                throw new AiProviderUnavailableException(install.IsDeployed ? DeployedFilesMissingMessage : NotInstalledMessage);
            }

            var loaded = await Task.Run(() => LoadModel(install.Directory), cancellationToken);
            _model = loaded;
            LogModelLoaded(_logger, loaded.SampleRate, install.IsDeployed);
            return loaded;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task RetireAsync(CancellationToken cancellationToken)
    {
        if (_model is null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            await RetireLockedAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Disposes the loaded sessions once no inference is running on them. Caller holds
    /// <see cref="_initLock"/>. Takes every synthesis slot, uncancellably so a cancelled request
    /// can't leave slots half-taken; an inference in flight is bounded by its own text chunk.</summary>
    private async Task RetireLockedAsync()
    {
        var retiring = _model;
        if (retiring is null)
        {
            return;
        }

        _model = null;
        for (var i = 0; i < _synthesisSlots; i++)
        {
            await _synthesisGate.WaitAsync(CancellationToken.None);
        }

        try
        {
            retiring.Dispose();
        }
        finally
        {
            _synthesisGate.Release(_synthesisSlots);
        }

        LogModelUnloaded(_logger);
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

    private LoadedModel LoadModel(string directory)
    {
        var onnxDirectory = Path.Combine(directory, "onnx");

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
            directory,
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Supertonic model loaded ({SampleRate} Hz, from a custom model deployment: {IsDeployed})")]
    private static partial void LogModelLoaded(ILogger logger, int sampleRate, bool isDeployed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Supertonic model sessions disposed")]
    private static partial void LogModelUnloaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Supertonic cannot speak: the custom model deployed from {RepositoryId} is marked unavailable")]
    private static partial void LogMarkedUnavailable(ILogger logger, string repositoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Supertonic model sessions were switched while a reply was speaking; failing the rest of that reply over")]
    private static partial void LogModelSwitched(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Supertonic inference failed")]
    private static partial void LogInferenceFailed(ILogger logger, Exception exception);

    internal sealed record VoiceStyle(float[] Ttl, int[] TtlDimensions, float[] Dp, int[] DpDimensions);

    internal sealed record LoadedModel(
        string Directory,
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
        private volatile bool _disposed;

        public bool IsDisposed => _disposed;

        public void Dispose()
        {
            _disposed = true;
            DurationPredictor.Dispose();
            TextEncoder.Dispose();
            VectorEstimator.Dispose();
            Vocoder.Dispose();
        }
    }
}

/// <summary>The folder one voice request uses and the voice styles installed in it.</summary>
internal sealed record SupertonicInstall(string Directory, bool IsDeployed, IReadOnlyList<string> Voices);

/// <summary>A loaded voice style and the sessions it was loaded with, ready to pass to <see cref="SupertonicModel.SynthesizeAsync"/>.</summary>
internal sealed record SupertonicVoice(string Id, int SampleRate, SupertonicModel.VoiceStyle Style, SupertonicModel.LoadedModel Model);
