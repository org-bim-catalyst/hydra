using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;

namespace AskLucy.Infrastructure.Ai;

internal static partial class WhisperProviderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading Whisper model {ModelSize} to {Path} (one-time, ~100-300MB)")]
    public static partial void DownloadingModel(ILogger logger, string modelSize, string path);
}

/// <summary>
/// Free, self-hosted alternative to a paid transcription API (see
/// <see cref="ITranscriptionProvider"/>'s doc comment for why this is a separate
/// abstraction from <see cref="IAIProvider"/>). Wraps Whisper.net (a .NET binding for
/// whisper.cpp, itself a port of OpenAI's open-sourced Whisper model) running entirely
/// in-process — no network call per request, only a one-time model download on first use.
/// </summary>
public sealed class WhisperLocalTranscriptionProvider : ITranscriptionProvider, IAsyncDisposable
{
    private readonly WhisperOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WhisperLocalTranscriptionProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private WhisperFactory? _factory;

    public WhisperLocalTranscriptionProvider(
        IOptions<WhisperOptions> options, IHostEnvironment environment, ILogger<WhisperLocalTranscriptionProvider> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(Stream wavAudio, CancellationToken cancellationToken = default)
    {
        var factory = await GetFactoryAsync(cancellationToken);

        using var processor = factory.CreateBuilder().WithLanguageDetection().Build();

        var segments = new List<string>();
        try
        {
            await foreach (var segment in processor.ProcessAsync(wavAudio, cancellationToken))
            {
                segments.Add(segment.Text.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AiProviderUnavailableException("Voice input failed. Please try again.", ex);
        }

        return string.Join(' ', segments).Trim();
    }

    /// <summary>Triggers the (otherwise lazy) model download/load in the background at
    /// startup, via <see cref="WhisperWarmupHostedService"/>, so it's already resident by
    /// the time a user first clicks the mic instead of stalling their first request.</summary>
    public Task WarmUpAsync(CancellationToken cancellationToken) => GetFactoryAsync(cancellationToken);

    private async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
        if (_factory is not null)
        {
            return _factory;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_factory is not null)
            {
                return _factory;
            }

            var modelPath = await EnsureModelDownloadedAsync(cancellationToken);
            _factory = WhisperFactory.FromPath(modelPath);
            return _factory;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AiProviderUnavailableException(
                "Voice transcription is unavailable (the local speech model couldn't be loaded).", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<string> EnsureModelDownloadedAsync(CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GgmlType>(_options.ModelSize, out var ggmlType))
        {
            ggmlType = GgmlType.BaseEn;
        }

        var directory = Path.Combine(_environment.ContentRootPath, _options.ModelDirectory);
        Directory.CreateDirectory(directory);

        var modelPath = Path.Combine(directory, $"ggml-{_options.ModelSize}.bin");
        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        WhisperProviderLog.DownloadingModel(_logger, _options.ModelSize, modelPath);

        var tempPath = modelPath + ".download";
        await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
            ggmlType, QuantizationType.NoQuantization, cancellationToken))
        await using (var fileStream = File.Create(tempPath))
        {
            await modelStream.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tempPath, modelPath);
        return modelPath;
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _initLock.Dispose();
        await Task.CompletedTask;
    }
}
