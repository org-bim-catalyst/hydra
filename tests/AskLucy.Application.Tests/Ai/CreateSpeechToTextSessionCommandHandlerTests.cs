using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/074 US2 — a failed transcription session is a failover on the operational
/// failure trail (the browser's recogniser serves the user), and the next minted session is its recovery.</summary>
public sealed class CreateSpeechToTextSessionCommandHandlerTests
{
    private readonly ISpeechToTextSessionProvider _sessionProvider = Substitute.For<ISpeechToTextSessionProvider>();
    private readonly IVoiceProviderHealthRecorder _healthRecorder = Substitute.For<IVoiceProviderHealthRecorder>();
    private readonly IVoiceProviderFailoverEventRepository _failoverEvents = Substitute.For<IVoiceProviderFailoverEventRepository>();
    private readonly IVoiceFailureReporter _reporter = Substitute.For<IVoiceFailureReporter>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public CreateSpeechToTextSessionCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _sessionProvider.ProviderName.Returns("ElevenLabs");
        _sessionProvider.IsSwitchedOnAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private CreateSpeechToTextSessionCommandHandler CreateHandler() =>
        new(_sessionProvider, _healthRecorder, _failoverEvents, _reporter, _currentUser);

    [Fact]
    public async Task AMintedSession_ShouldBeReportedAsServed()
    {
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).Returns(new SpeechToTextSession("token", DateTime.UtcNow));

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

        session.Engine.Should().Be(DictationEngine.Realtime);
        session.Token.Should().Be("token");
        _reporter.Received(1).ReportServed(VoiceOperations.Transcription, new VoiceEngineIdentity("ElevenLabs"));
    }

    [Fact]
    public async Task AFailedSession_ShouldBeReportedAsADegradedFailover_AndStillRethrown()
    {
        var failure = new AiProviderAuthenticationException("The voice provider rejected the credential.");
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var act = () => CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderAuthenticationException>();
        _reporter.Received(1).ReportFailover(
            VoiceOperations.Transcription, new VoiceEngineIdentity("ElevenLabs"), failure, true, Arg.Any<CancellationToken>());
        await _healthRecorder.Received(1).RecordFailoverAsync("user-1", failure.Message, Arg.Any<CancellationToken>());
        _reporter.DidNotReceiveWithAnyArgs().ReportServed(default!, default!);
    }

    [Fact]
    public async Task ASwitchedOffProvider_ShouldHandDictationToWhisper_WithoutRecordingAFailure()
    {
        _sessionProvider.IsSwitchedOnAsync(Arg.Any<CancellationToken>()).Returns(false);

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

        // An admin switching ElevenLabs off is a choice, not an outage: Whisper is the normal
        // engine, so there is no failover, no health change and no operational failure.
        session.Should().Be(DictationSession.Whisper);
        await _sessionProvider.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, TestContext.Current.CancellationToken);
        _reporter.DidNotReceiveWithAnyArgs().ReportFailover(default!, default!, default!, default, TestContext.Current.CancellationToken);
        _reporter.DidNotReceiveWithAnyArgs().ReportServed(default!, default!);
        await _healthRecorder.DidNotReceiveWithAnyArgs().RecordFailoverAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
