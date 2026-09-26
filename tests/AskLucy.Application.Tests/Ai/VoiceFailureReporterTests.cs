using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/074 US2 — voice failovers go on the trail, and the engine serving the same user
/// again afterwards is a recovery that rebuilds the failover's grouping key.</summary>
public sealed class VoiceFailureReporterTests
{
    private static readonly VoiceEngineIdentity ElevenLabs = new("ElevenLabs", Guid.NewGuid(), "eleven_flash_v2_5");

    private readonly IOperationalFailureRecorder _recorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly VoiceFailoverMemory _memory = new();

    public VoiceFailureReporterTests() => _currentUser.UserId.Returns("user-1");

    private VoiceFailureReporter CreateReporter() => new(_recorder, new FailureClassifier(), _memory, _currentUser);

    [Fact]
    public void ReportFailover_ShouldRecordAVoiceFailover_AndMarkTheException()
    {
        var failure = new AiProviderAuthenticationException("The voice provider rejected the credential.");

        CreateReporter().ReportFailover(VoiceOperations.TextToSpeech, ElevenLabs, failure, fallbackServed: true, CancellationToken.None);

        var report = (OperationalFailureReport)_recorder.ReceivedCalls().Single().GetArguments()[0]!;
        report.Engine.Should().Be(OperationalFailureEngine.Voice);
        report.Operation.Should().Be(VoiceOperations.TextToSpeech);
        report.Kind.Should().Be(OperationalFailureKind.CredentialRejected);
        report.Outcome.Should().Be(OperationalFailureOutcome.DegradedServed);
        report.Reason.Should().Be("The voice provider rejected the credential.");
        report.ProviderId.Should().Be(ElevenLabs.ProviderId);
        report.ProviderName.Should().Be("ElevenLabs");
        report.Model.Should().Be("eleven_flash_v2_5");
        report.References.UserId.Should().Be("user-1");
        report.IsFailover.Should().BeTrue();
        failure.IsOperationalFailureRecorded().Should().BeTrue();
    }

    [Fact]
    public void ReportFailover_ShouldRecordFailed_WhenNothingServedTheUser()
    {
        CreateReporter().ReportFailover(
            VoiceOperations.TextToSpeech, ElevenLabs, new AiProviderUnavailableException("down"), fallbackServed: false, CancellationToken.None);

        _recorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r => r.Outcome == OperationalFailureOutcome.Failed));
    }

    [Fact]
    public void ReportFailover_ShouldNameANonProviderExceptionByTypeOnly()
    {
        CreateReporter().ReportFailover(
            VoiceOperations.Transcription, ElevenLabs, new InvalidOperationException("detail"), fallbackServed: true, CancellationToken.None);

        _recorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r => r.Reason == nameof(InvalidOperationException)));
    }

    [Fact]
    public void ReportServed_AfterAFailover_ShouldRecordOneRecoveryWithTheFailoversKeyInputs()
    {
        var reporter = CreateReporter();
        reporter.ReportFailover(
            VoiceOperations.TextToSpeech, ElevenLabs, new AiProviderAuthenticationException("x"), fallbackServed: true, CancellationToken.None);

        // The model the serving attempt used does not matter: the recovery replays the failover's.
        reporter.ReportServed(VoiceOperations.TextToSpeech, ElevenLabs with { Model = "another" });
        reporter.ReportServed(VoiceOperations.TextToSpeech, ElevenLabs);

        _recorder.Received(1).RecordRecovery(Arg.Is<VoiceRecoveryReport>(r =>
            r.Operation == VoiceOperations.TextToSpeech &&
            r.Kind == OperationalFailureKind.CredentialRejected &&
            r.ProviderName == "ElevenLabs" &&
            r.Model == "eleven_flash_v2_5" &&
            r.UserId == "user-1"));
    }

    [Fact]
    public void ReportServed_ShouldRecordNothing_WithoutAMatchingFailover()
    {
        var reporter = CreateReporter();
        reporter.ReportFailover(
            VoiceOperations.TextToSpeech, ElevenLabs, new AiProviderAuthenticationException("x"), fallbackServed: true, CancellationToken.None);

        reporter.ReportServed(VoiceOperations.TextToSpeech, new VoiceEngineIdentity("Supertonic"));
        reporter.ReportServed(VoiceOperations.Transcription, ElevenLabs);

        _recorder.DidNotReceiveWithAnyArgs().RecordRecovery(default!);
    }

    [Fact]
    public void ReportServed_ShouldPairWithTheSameUsersFailoverOnly()
    {
        CreateReporter().ReportFailover(
            VoiceOperations.TextToSpeech, ElevenLabs, new AiProviderAuthenticationException("x"), fallbackServed: true, CancellationToken.None);

        _currentUser.UserId.Returns("user-2");
        CreateReporter().ReportServed(VoiceOperations.TextToSpeech, ElevenLabs);

        _recorder.DidNotReceiveWithAnyArgs().RecordRecovery(default!);
    }

    [Fact]
    public void TheCallersOwnCancellation_ShouldRecordNothing_AndRememberNothing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var reporter = CreateReporter();

        reporter.ReportFailover(VoiceOperations.TextToSpeech, ElevenLabs, new OperationCanceledException(), fallbackServed: true, cancelled.Token);
        reporter.ReportServed(VoiceOperations.TextToSpeech, ElevenLabs);

        _recorder.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void ReportFailure_ShouldRecordAFailedNonFailover_EvenWithNoEngine()
    {
        CreateReporter().ReportFailure(
            VoiceOperations.TextToSpeech, null, new AiProviderNotConfiguredException("No voice provider is configured."), CancellationToken.None);

        _recorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r =>
            r.Kind == OperationalFailureKind.NotConfigured &&
            r.Outcome == OperationalFailureOutcome.Failed &&
            !r.IsFailover &&
            r.ProviderName == null));
    }
}
