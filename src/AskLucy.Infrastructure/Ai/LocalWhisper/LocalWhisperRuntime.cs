using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>
/// specs/078 research D1 — Local Whisper, in-process. A singleton owning one loaded model, keyed by
/// file path and loaded on the first clip rather than at startup. When the selected model changes,
/// the next clip loads the new one; a clip already running finishes on the model it started with,
/// and the old model is released once the last such clip ends. Concurrent transcriptions are
/// capped; a clip that can't get a slot in time fails as unavailable, so the user falls to the
/// browser built-in (FR-005b). <see cref="LocalWhisperTranscriber"/> and
/// <see cref="LocalWhisperModelTrial"/> share it, so a try counts against the same cap.
/// </summary>
public sealed partial class LocalWhisperRuntime : IDisposable
{
    private readonly IWhisperModelLoader _loader;
    private readonly ILogger<LocalWhisperRuntime> _logger;
    private readonly TimeSpan _queueTimeout;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly SemaphoreSlim _trialLock = new(1, 1);
    private readonly Lock _gate = new();
    private LoadedModel? _current;

    public LocalWhisperRuntime(IOptions<LocalWhisperOptions> options, IWhisperModelLoader loader, ILogger<LocalWhisperRuntime> logger)
    {
        _loader = loader;
        _logger = logger;
        _queueTimeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.QueueTimeoutSeconds));
        var slots = Math.Max(1, options.Value.MaxConcurrentTranscriptions);
        _slots = new SemaphoreSlim(slots, slots);
    }

    public async Task<DictationTranscript> TranscribeAsync(string modelPath, Stream wav, string? language, CancellationToken cancellationToken = default)
    {
        await EnterSlotAsync(cancellationToken);
        try
        {
            var model = await AcquireAsync(modelPath, cancellationToken);
            try
            {
                return await RunAsync(model.Model, wav, language, cancellationToken);
            }
            finally
            {
                Release(model);
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    public async Task<DictationTranscript> TryAsync(string modelPath, Stream wav, string? language, CancellationToken cancellationToken = default)
    {
        if (!await _trialLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new LocalWhisperTrialBusyException();
        }

        try
        {
            await EnterSlotAsync(cancellationToken);
            try
            {
                // Trying the model that already serves reuses it; any other model is loaded for
                // this try only, so trying never changes what users are served (FR-009c).
                var shared = TryLeaseCurrent(modelPath);
                if (shared is not null)
                {
                    try
                    {
                        return await RunAsync(shared.Model, wav, language, cancellationToken);
                    }
                    finally
                    {
                        Release(shared);
                    }
                }

                using var trial = Load(modelPath);
                return await RunAsync(trial, wav, language, cancellationToken);
            }
            finally
            {
                _slots.Release();
            }
        }
        finally
        {
            _trialLock.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _current?.Model.Dispose();
            _current = null;
        }

        _slots.Dispose();
        _loadLock.Dispose();
        _trialLock.Dispose();
    }

    private async Task EnterSlotAsync(CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(_queueTimeout, cancellationToken))
        {
            LogQueueFull(_logger, _queueTimeout.TotalSeconds);
            throw new AiProviderUnavailableException("Local Whisper is busy. Please try again.");
        }
    }

    private static async Task<DictationTranscript> RunAsync(IWhisperModel model, Stream wav, string? language, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var segments = new List<string>();
        try
        {
            await foreach (var segment in model.TranscribeAsync(wav, language, cancellationToken))
            {
                var text = segment.Trim();
                if (text.Length > 0)
                {
                    segments.Add(text);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AiProviderException)
        {
            throw new AiProviderUnavailableException("Local Whisper couldn't transcribe the clip.", ex);
        }

        return new DictationTranscript(string.Join(' ', segments), string.IsNullOrWhiteSpace(language) ? null : language, stopwatch.Elapsed);
    }

    private async Task<LoadedModel> AcquireAsync(string modelPath, CancellationToken cancellationToken)
    {
        var current = TryLeaseCurrent(modelPath);
        if (current is not null)
        {
            return current;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            current = TryLeaseCurrent(modelPath);
            if (current is not null)
            {
                return current;
            }

            var loaded = new LoadedModel(modelPath, Load(modelPath)) { Leases = 1 };
            LoadedModel? retired;
            lock (_gate)
            {
                retired = _current;
                _current = loaded;
                if (retired is not null)
                {
                    retired.Retired = true;
                    if (retired.Leases > 0)
                    {
                        // Its last running clip disposes it (Release).
                        retired = null;
                    }
                }
            }

            if (retired is not null)
            {
                retired.Model.Dispose();
                LogModelReleased(_logger, retired.FileName);
            }

            LogModelLoaded(_logger, loaded.FileName);
            return loaded;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private LoadedModel? TryLeaseCurrent(string modelPath)
    {
        lock (_gate)
        {
            if (_current is { } current && string.Equals(current.Path, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                current.Leases++;
                return current;
            }

            return null;
        }
    }

    private void Release(LoadedModel model)
    {
        bool dispose;
        lock (_gate)
        {
            model.Leases--;
            dispose = model.Retired && model.Leases == 0;
        }

        if (dispose)
        {
            model.Model.Dispose();
            LogModelReleased(_logger, model.FileName);
        }
    }

    private IWhisperModel Load(string modelPath)
    {
        try
        {
            return _loader.Load(modelPath);
        }
        catch (Exception ex)
        {
            LogModelLoadFailed(_logger, ex, Path.GetFileName(modelPath));
            throw new AiProviderUnavailableException("The Local Whisper model couldn't be loaded.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local Whisper model {FileName} loaded")]
    private static partial void LogModelLoaded(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local Whisper model {FileName} released after the selection changed")]
    private static partial void LogModelReleased(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Local Whisper model {FileName} failed to load")]
    private static partial void LogModelLoadFailed(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local Whisper had no free transcription slot within {Seconds} s")]
    private static partial void LogQueueFull(ILogger logger, double seconds);

    private sealed class LoadedModel(string path, IWhisperModel model)
    {
        public string Path { get; } = path;

        public string FileName { get; } = System.IO.Path.GetFileName(path);

        public IWhisperModel Model { get; } = model;

        public int Leases { get; set; }

        public bool Retired { get; set; }
    }
}
