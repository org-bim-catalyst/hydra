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
    }

    private CreateSpeechToTextSessionCommandHandler CreateHandler() =>
        new(_sessionProvider, _healthRecorder, _failoverEvents, _reporter, _currentUser);

    [Fact]
    public async Task AMintedSession_ShouldBeReportedAsServed()
    {
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).Returns(new SpeechToTextSession("token", DateTime.UtcNow));

        await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

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
}
