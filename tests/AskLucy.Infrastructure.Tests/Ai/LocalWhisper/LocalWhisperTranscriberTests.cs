using System.Runtime.CompilerServices;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai.LocalWhisper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.LocalWhisper;

/// <summary>specs/078 research D1 — lazy load, model swap with leases, the concurrency cap and the try path, with no real model.</summary>
public sealed class LocalWhisperTranscriberTests : IDisposable
{
    private readonly FakeLoader _loader = new();
    private readonly LocalWhisperRuntime _runtime;

    public LocalWhisperTranscriberTests()
    {
        _runtime = new LocalWhisperRuntime(
            Options.Create(new LocalWhisperOptions { MaxConcurrentTranscriptions = 1, QueueTimeoutSeconds = 1 }),
            _loader,
            NullLogger<LocalWhisperRuntime>.Instance);
    }

    public void Dispose() => _runtime.Dispose();

    private static DictationClip Clip(string modelPath, string? language = null) => new(new MemoryStream([1, 2, 3]), language, modelPath);

    [Fact]
    public async Task TranscribeAsync_ShouldLoadOnTheFirstClipOnly_AndJoinTrimmedSegments()
    {
        var transcriber = new LocalWhisperTranscriber(_runtime);
        _loader.Loaded.Should().BeEmpty();

        var first = await transcriber.TranscribeAsync(Clip("a.bin", "ar"), TestContext.Current.CancellationToken);
        await transcriber.TranscribeAsync(Clip("A.BIN"), TestContext.Current.CancellationToken);

        first.Text.Should().Be("hello world");
        first.DetectedLanguage.Should().Be("ar");
        _loader.Loaded.Should().ContainSingle().Which.Path.Should().Be("a.bin");
        _loader.Loaded[0].Languages.Should().Equal("ar", null);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldRequireTheResolvedModelPath()
    {
        var transcriber = new LocalWhisperTranscriber(_runtime);

        var act = () => transcriber.TranscribeAsync(new DictationClip(new MemoryStream(), null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TranscribeAsync_ShouldDisposeTheOldModel_OnlyAfterItsRunningClipEnds()
    {
        var runtime = new LocalWhisperRuntime(
            Options.Create(new LocalWhisperOptions { MaxConcurrentTranscriptions = 2, QueueTimeoutSeconds = 1 }),
            _loader,
            NullLogger<LocalWhisperRuntime>.Instance);
        using var _ = runtime;
        var gate = new TaskCompletionSource();
        _loader.Gate = gate.Task;

        var running = runtime.TranscribeAsync("old.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);
        await _loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _loader.Gate = Task.CompletedTask;

        await runtime.TranscribeAsync("new.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);
        _loader.Loaded.Select(m => m.Path).Should().Equal("old.bin", "new.bin");
        _loader.Loaded[0].Disposed.Should().BeFalse("its clip is still running");

        gate.SetResult();
        await running;
        _loader.Loaded[0].Disposed.Should().BeTrue();
        _loader.Loaded[1].Disposed.Should().BeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_ShouldFailAsUnavailable_WhenNoSlotFreesUpInTime()
    {
        var gate = new TaskCompletionSource();
        _loader.Gate = gate.Task;
        var running = _runtime.TranscribeAsync("a.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);
        await _loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var act = () => _runtime.TranscribeAsync("a.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>()).Which.Kind.Should().Be(AiProviderFailureKind.Unavailable);
        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task TranscribeAsync_ShouldWrapALoadFailureAsUnavailable()
    {
        _loader.FailLoad = true;

        var act = () => _runtime.TranscribeAsync("a.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AiProviderUnavailableException>().WithMessage("The Local Whisper model couldn't be loaded.");
    }

    [Fact]
    public async Task TryAsync_ShouldUseATransientModel_AndNeverReplaceTheServingOne()
    {
        await _runtime.TranscribeAsync("serving.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);

        var result = await new LocalWhisperModelTrial(_runtime).TryAsync("candidate.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);
        await _runtime.TranscribeAsync("serving.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);

        result.Text.Should().Be("hello world");
        _loader.Loaded.Select(m => m.Path).Should().Equal("serving.bin", "candidate.bin");
        _loader.Loaded[1].Disposed.Should().BeTrue();
        _loader.Loaded[0].Disposed.Should().BeFalse();
    }

    [Fact]
    public async Task TryAsync_ShouldRefuseASecondTry_WhileOneIsRunning()
    {
        var gate = new TaskCompletionSource();
        _loader.Gate = gate.Task;
        var trial = new LocalWhisperModelTrial(_runtime);
        var running = trial.TryAsync("a.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);
        await _loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var act = () => trial.TryAsync("a.bin", new MemoryStream(), null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LocalWhisperTrialBusyException>();
        gate.SetResult();
        await running;
    }

    private sealed class FakeLoader : IWhisperModelLoader
    {
        public List<FakeModel> Loaded { get; } = [];

        public Task Gate { get; set; } = Task.CompletedTask;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailLoad { get; set; }

        public IWhisperModel Load(string modelPath)
        {
            if (FailLoad)
            {
                throw new IOException("bad file");
            }

            var model = new FakeModel(modelPath, this);
            Loaded.Add(model);
            return model;
        }
    }

    private sealed class FakeModel(string path, FakeLoader loader) : IWhisperModel
    {
        public string Path { get; } = path;

        public List<string?> Languages { get; } = [];

        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<string> TranscribeAsync(Stream wav, string? language, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Languages.Add(language);
            var gate = loader.Gate;
            loader.Started.TrySetResult();
            await gate.WaitAsync(cancellationToken);
            yield return " hello ";
            yield return "  ";
            yield return "world ";
        }

        public void Dispose() => Disposed = true;
    }
}
