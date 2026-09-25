using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 research D3/D18 — sanitise, derive severity, key, append; the pipeline's own failures reach the log only.</summary>
public sealed class OperationalFailureIngestorTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 10, 15, 0, DateTimeKind.Utc);

    private readonly IOperationalFailureStore _store = Substitute.For<IOperationalFailureStore>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly FakeLogger<OperationalFailureIngestor> _logger = new();
    private readonly List<IncidentAppendRequest> _appended = [];

    public OperationalFailureIngestorTests()
    {
        _store.AppendAsync(Arg.Do<IncidentAppendRequest>(_appended.Add), Arg.Any<CancellationToken>())
            .Returns(call => new IncidentAppendResult(Guid.NewGuid(), false, call.Arg<IncidentAppendRequest>()!.Severity));
    }

    private OperationalFailureIngestor CreateIngestor() => new(_store, _publisher, _logger);

    private static OperationalFailureReport Report(
        OperationalFailureKind kind = OperationalFailureKind.CredentialRejected,
        string reason = "Text-to-speech request failed",
        string? correlationId = "corr-1",
        OperationalFailureOutcome outcome = OperationalFailureOutcome.Failed,
        Exception? exception = null) => new()
        {
            Engine = OperationalFailureEngine.Voice,
            Operation = "Text-to-speech",
            Kind = kind,
            Outcome = outcome,
            Reason = reason,
            Exception = exception,
            ProviderName = "ElevenLabs",
            CorrelationId = correlationId,
            OccurredAtUtc = Now,
        };

    [Fact]
    public async Task IngestAsync_SanitisesTheReasonBeforeTheStoreSeesIt()
    {
        await CreateIngestor().IngestAsync([Report(reason: "401 from vendor, key sk-ant-api03-Zx9Yw8Vu7Ts6Rq5Po4\nretrying")], TestContext.Current.CancellationToken);

        _appended.Should().ContainSingle();
        _appended[0].Reason.Should().NotContain("Zx9Yw8").And.NotContain("\n").And.Contain(FailureReasonSanitizer.Redacted);
    }

    [Fact]
    public async Task IngestAsync_FallsBackToTheExceptionTypeNameWhenNoReasonSurvives()
    {
        await CreateIngestor().IngestAsync([Report(reason: "  ", exception: new InvalidOperationException("secret detail"))], TestContext.Current.CancellationToken);

        _appended[0].Reason.Should().Be(nameof(InvalidOperationException));
    }

    [Theory]
    [InlineData(OperationalFailureKind.CredentialRejected, OperationalFailureOutcome.DegradedServed, OperationalFailureSeverity.Critical)]
    [InlineData(OperationalFailureKind.RateLimited, OperationalFailureOutcome.Failed, OperationalFailureSeverity.Warning)]
    [InlineData(OperationalFailureKind.Unavailable, OperationalFailureOutcome.Failed, OperationalFailureSeverity.Error)]
    [InlineData(OperationalFailureKind.Unavailable, OperationalFailureOutcome.DegradedServed, OperationalFailureSeverity.Warning)]
    public async Task IngestAsync_DerivesSeverityFromThePolicy(OperationalFailureKind kind, OperationalFailureOutcome outcome, OperationalFailureSeverity expected)
    {
        await CreateIngestor().IngestAsync([Report(kind: kind, outcome: outcome)], TestContext.Current.CancellationToken);

        _appended[0].Severity.Should().Be(expected);
    }

    [Fact]
    public async Task IngestAsync_PassesTheKeysAndCallerStampedValues()
    {
        var report = Report();

        await CreateIngestor().IngestAsync([report], TestContext.Current.CancellationToken);

        _appended[0].GroupingKey.Should().Be(OperationalFailureKeys.Grouping(report));
        _appended[0].RootCauseKey.Should().Be(OperationalFailureKeys.RootCause(report));
        _appended[0].CorrelationId.Should().Be("corr-1");
        _appended[0].OccurredAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task IngestAsync_GeneratesAndLogsACorrelationIdWhenNoneWasCaptured()
    {
        await CreateIngestor().IngestAsync([Report(correlationId: null)], TestContext.Current.CancellationToken);

        var generated = _appended[0].CorrelationId;
        generated.Should().NotBeNullOrWhiteSpace();
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains(generated));
    }

    [Fact]
    public async Task IngestAsync_LogsAStoreFailureWithoutRethrowingAndContinues()
    {
        _store.AppendAsync(Arg.Is<IncidentAppendRequest>(r => r!.CorrelationId == "bad"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var results = await CreateIngestor().IngestAsync([Report(correlationId: "bad"), Report(correlationId: "good")], TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        var error = _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error).Subject;
        error.Message.Should().Contain("bad").And.Contain("Voice").And.Contain("CredentialRejected");
        error.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task IngestAsync_PublishesCriticalIncidentOpenedOnlyForANewCriticalIncident()
    {
        var opened = Guid.NewGuid();
        _store.AppendAsync(Arg.Is<IncidentAppendRequest>(r => r!.CorrelationId == "new"), Arg.Any<CancellationToken>())
            .Returns(new IncidentAppendResult(opened, true, OperationalFailureSeverity.Critical));
        _store.AppendAsync(Arg.Is<IncidentAppendRequest>(r => r!.CorrelationId == "new-error"), Arg.Any<CancellationToken>())
            .Returns(new IncidentAppendResult(Guid.NewGuid(), true, OperationalFailureSeverity.Error));

        await CreateIngestor().IngestAsync([Report(correlationId: "new"), Report(correlationId: "new-error"), Report(correlationId: "joined")], TestContext.Current.CancellationToken);

        await _publisher.Received(1).Publish(Arg.Any<CriticalIncidentOpened>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<CriticalIncidentOpened>(n => n!.IncidentId == opened && n.CorrelationId == "new"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_ANotificationFailureIsLoggedAndDoesNotLoseTheResult()
    {
        _store.AppendAsync(Arg.Any<IncidentAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentAppendResult(Guid.NewGuid(), true, OperationalFailureSeverity.Critical));
        _publisher.Publish(Arg.Any<CriticalIncidentOpened>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("handler"));

        var results = await CreateIngestor().IngestAsync([Report()], TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task IngestAsync_IncrementsRecoveryOnTheFailoverIncidentsKey()
    {
        var recovery = new VoiceRecoveryReport
        {
            ProviderName = "ElevenLabs",
            Kind = OperationalFailureKind.CredentialRejected,
            Operation = "Text-to-speech",
            CorrelationId = "corr-r",
        };

        var results = await CreateIngestor().IngestAsync([recovery], TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
        await _store.Received(1).IncrementRecoveryAsync(OperationalFailureKeys.Grouping(recovery), Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IngestAsync_ARecoveryFailureIsLoggedAndNotRethrown()
    {
        _store.IncrementRecoveryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => CreateIngestor().IngestAsync([new VoiceRecoveryReport { Kind = OperationalFailureKind.CredentialRejected, Operation = "Text-to-speech" }], TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error);
    }

    [Fact]
    public void Ingestor_NeverDependsOnTheRecorder()
    {
        // FR-021: the pipeline must never record its own failures, so it cannot even reach the recorder.
        var parameterTypes = typeof(OperationalFailureIngestor).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        parameterTypes.Should().NotContain(typeof(IOperationalFailureRecorder));
    }
}
