using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Infrastructure.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.OperationalFailures;

/// <summary>specs/074 T012 — guarantees G1–G4 of contracts/operational-failure-recorder.md.</summary>
public sealed class ChannelOperationalFailureRecorderTests
{
    private readonly ICorrelationIdAccessor _correlation = Substitute.For<ICorrelationIdAccessor>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<ChannelOperationalFailureRecorder> _logger = new();

    [Fact]
    public void Record_ShouldReturnImmediately_WhenNothingEverReads()
    {
        var recorder = Create(capacity: 1000);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            recorder.Record(Report());
        }

        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100), "100 records must not wait on any reader (G1)");
        recorder.Reader.Count.Should().Be(100);
    }

    [Fact]
    public void Record_ShouldDropAndLog_WhenTheQueueIsFull()
    {
        _correlation.Current.Returns("corr-full");
        var recorder = Create(capacity: 1);

        recorder.Record(Report());
        recorder.Record(Report());

        recorder.Reader.Count.Should().Be(1);
        var dropped = _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning).Subject;
        dropped.Message.Should().Contain("corr-full").And.Contain("Voice").And.Contain("CredentialRejected");
    }

    [Fact]
    public void Record_ShouldNotThrow_ForANullReport()
    {
        var recorder = Create();

        var act = () => recorder.Record(null!);

        act.Should().NotThrow();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error);
        recorder.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void Record_ShouldNotThrow_WhenTheLoggerThrows()
    {
        var logger = Substitute.For<ILogger<ChannelOperationalFailureRecorder>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
#pragma warning disable CA1873 // Arranging a substitute, not logging.
        logger.When(l => l.Log(Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>()))
            .Do(_ => throw new InvalidOperationException("sink down"));
#pragma warning restore CA1873
        var recorder = new ChannelOperationalFailureRecorder(Options(capacity: 1), _correlation, _time, logger);

        var act = () =>
        {
            recorder.Record(Report());
            recorder.Record(Report());
            recorder.Record(null!);
            recorder.RecordRecovery(null!);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Record_ShouldCaptureTheCorrelationIdAndTime_OnTheCallersThread()
    {
        _correlation.Current.Returns("first");
        var recorder = Create();

        recorder.Record(Report() with { CorrelationId = "caller-set", OccurredAtUtc = DateTime.MinValue });
        _correlation.Current.Returns("second");
        _time.Advance(TimeSpan.FromMinutes(5));

        recorder.Reader.TryRead(out var signal).Should().BeTrue();
        signal!.CorrelationId.Should().Be("first");
        signal.OccurredAtUtc.Should().Be(new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc));
        signal.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void RecordRecovery_ShouldQueueAStampedRecovery()
    {
        _correlation.Current.Returns("corr-recovery");
        var recorder = Create();

        recorder.RecordRecovery(new VoiceRecoveryReport { Operation = "Text-to-speech", Kind = OperationalFailureKind.CredentialRejected });

        recorder.Reader.TryRead(out var signal).Should().BeTrue();
        signal.Should().BeOfType<VoiceRecoveryReport>().Which.CorrelationId.Should().Be("corr-recovery");
    }

    private ChannelOperationalFailureRecorder Create(int capacity = 100) =>
        new(Options(capacity), _correlation, _time, _logger);

    private static IOptions<OperationalFailuresOptions> Options(int capacity) =>
        Microsoft.Extensions.Options.Options.Create(new OperationalFailuresOptions { QueueCapacity = capacity });

    private static OperationalFailureReport Report() => new()
    {
        Engine = OperationalFailureEngine.Voice,
        Operation = "Text-to-speech",
        Kind = OperationalFailureKind.CredentialRejected,
        Reason = "The provider rejected the credential.",
    };
}
