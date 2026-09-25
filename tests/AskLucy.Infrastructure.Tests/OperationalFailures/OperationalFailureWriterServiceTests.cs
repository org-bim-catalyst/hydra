using System.Collections.Concurrent;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Infrastructure.OperationalFailures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.OperationalFailures;

/// <summary>specs/074 T013 — the single reader that moves queued reports into the store (research D2).</summary>
public sealed class OperationalFailureWriterServiceTests
{
    private readonly FakeLogger<OperationalFailureWriterService> _writerLogger = new();

    [Fact]
    public async Task Writer_ShouldWriteInBatchesOfAtMostTheBatchSize_EachInItsOwnScope()
    {
        var store = new RecordingStoreLog();
        var (recorder, provider) = Build(batchSize: 3, services => services.AddScoped<IOperationalFailureStore>(_ => new RecordingStore(store)));
        for (var i = 0; i < 7; i++)
        {
            recorder.Record(Report());
        }

        using var writer = CreateWriter(recorder, provider, batchSize: 3);
        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => store.Appends.Count == 7);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        var perScope = store.Appends.GroupBy(a => a.StoreInstance).Select(g => g.Count()).ToList();
        perScope.Should().HaveCountGreaterThanOrEqualTo(3, "7 reports in batches of 3 need at least 3 batches, each with a new scope");
        perScope.Should().OnlyContain(count => count <= 3);
    }

    [Fact]
    public async Task Writer_ShouldLogEveryReportOfAFailedBatch_AndCarryOn()
    {
        var store = new RecordingStoreLog();
        var scopes = 0;
        var (recorder, provider) = Build(batchSize: 2, services => services.AddScoped<IOperationalFailureStore>(_ =>
            Interlocked.Increment(ref scopes) == 1 ? throw new InvalidOperationException("scope broken") : new RecordingStore(store)));
        var correlation = provider.GetRequiredService<ICorrelationIdAccessor>();
        correlation.Current.Returns("corr-1", "corr-2", "corr-3");
        recorder.Record(Report());
        recorder.Record(Report());
        recorder.Record(Report());

        using var writer = CreateWriter(recorder, provider, batchSize: 2);
        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => store.Appends.Count == 1);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        var errors = _writerLogger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Error).ToList();
        errors.Should().HaveCount(2);
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("corr-1")).And.Contain(m => m.Contains("corr-2"));
    }

    [Fact]
    public async Task StopAsync_ShouldDrainUntilTheDeadline_ThenLogWhatWasAbandoned()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (recorder, provider) = Build(batchSize: 1, services => services.AddScoped<IOperationalFailureStore>(_ => new BlockingStore(entered)));
        for (var i = 0; i < 5; i++)
        {
            recorder.Record(Report());
        }

        using var writer = CreateWriter(recorder, provider, batchSize: 1, drainTimeout: TimeSpan.FromMilliseconds(200));
        await writer.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopping = writer.StopAsync(TestContext.Current.CancellationToken);
        (await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().BeSameAs(stopping, "the drain is bounded by its deadline");

        _writerLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("4 report(s)");
    }

    [Fact]
    public async Task StopAsync_ShouldWriteWhatIsStillQueued_WhenTheStoreKeepsUp()
    {
        var store = new RecordingStoreLog();
        var (recorder, provider) = Build(batchSize: 10, services => services.AddScoped<IOperationalFailureStore>(_ => new RecordingStore(store)));

        using var writer = CreateWriter(recorder, provider, batchSize: 10);
        await writer.StartAsync(TestContext.Current.CancellationToken);
        recorder.Record(Report());
        recorder.Record(Report());
        await writer.StopAsync(TestContext.Current.CancellationToken);

        store.Appends.Should().HaveCount(2);
        _writerLogger.Collector.GetSnapshot().Should().NotContain(r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task StopAsync_ShouldStillWriteTheQueue_WhenTheHostStopsBeforeTheLoopEverRan()
    {
        var store = new RecordingStoreLog();
        var (recorder, provider) = Build(batchSize: 10, services => services.AddScoped<IOperationalFailureStore>(_ => new RecordingStore(store)));
        recorder.Record(Report());
        recorder.Record(Report());

        using var writer = CreateWriter(recorder, provider, batchSize: 10);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        store.Appends.Should().HaveCount(2);
    }

    private static (ChannelOperationalFailureRecorder Recorder, ServiceProvider Provider) Build(int batchSize, Action<IServiceCollection> addStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICorrelationIdAccessor>());
        services.AddSingleton(Substitute.For<IPublisher>());
        services.AddSingleton<ILoggerFactory>(new LoggerFactory());
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddScoped<OperationalFailureIngestor>();
        addStore(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var recorder = new ChannelOperationalFailureRecorder(
            Options.Create(new OperationalFailuresOptions { WriterBatchSize = batchSize }),
            provider.GetRequiredService<ICorrelationIdAccessor>(),
            TimeProvider.System,
            new FakeLogger<ChannelOperationalFailureRecorder>());
        return (recorder, provider);
    }

    private OperationalFailureWriterService CreateWriter(
        ChannelOperationalFailureRecorder recorder, ServiceProvider provider, int batchSize, TimeSpan? drainTimeout = null) =>
        new(recorder, provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new OperationalFailuresOptions { WriterBatchSize = batchSize }), _writerLogger)
        {
            DrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(5),
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the writer should have caught up by now");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static OperationalFailureReport Report() => new()
    {
        Engine = OperationalFailureEngine.Voice,
        Operation = "Text-to-speech",
        Kind = OperationalFailureKind.CredentialRejected,
        Reason = "The provider rejected the credential.",
    };

    private sealed class RecordingStoreLog
    {
        public ConcurrentQueue<(object StoreInstance, IncidentAppendRequest Request)> Appends { get; } = new();
    }

    private sealed class RecordingStore(RecordingStoreLog log) : IOperationalFailureStore
    {
        public Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request, CancellationToken cancellationToken = default)
        {
            log.Appends.Enqueue((this, request));
            return Task.FromResult(new IncidentAppendResult(Guid.NewGuid(), Opened: false, request.Severity));
        }

        public Task<bool> IncrementRecoveryAsync(string groupingKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class BlockingStore(TaskCompletionSource entered) : IOperationalFailureStore
    {
        public async Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public Task<bool> IncrementRecoveryAsync(string groupingKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
