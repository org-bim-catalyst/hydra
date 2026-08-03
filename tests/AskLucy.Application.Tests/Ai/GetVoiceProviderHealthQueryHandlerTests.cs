using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Queries.GetVoiceProviderHealth;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/012-elevenlabs-voice-engine T075 — contracts/voice-provider-health.md's
/// derived currentStatus/failoverCount/recoveryCount.</summary>
public sealed class GetVoiceProviderHealthQueryHandlerTests
{
    private readonly IVoiceProviderFailoverEventRepository _failoverEvents = Substitute.For<IVoiceProviderFailoverEventRepository>();
    private readonly GetVoiceProviderHealthQueryHandler _handler;

    public GetVoiceProviderHealthQueryHandlerTests()
    {
        _handler = new GetVoiceProviderHealthQueryHandler(_failoverEvents);
    }

    private static VoiceProviderFailoverEvent MakeEvent(DateTime occurredAtUtc, VoiceProviderFailoverDirection direction, string? reason = null) =>
        VoiceProviderFailoverEvent.Create("user-1", occurredAtUtc, direction, reason, "test");

    [Fact]
    public async Task Handle_ShouldReportDegraded_WhenMostRecentEventIsAnUnresolvedFailover()
    {
        var now = DateTime.UtcNow;
        _failoverEvents.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([MakeEvent(now.AddMinutes(-5), VoiceProviderFailoverDirection.FailedOverToFallback, "stt timeout")]);

        var result = await _handler.Handle(new GetVoiceProviderHealthQuery(null, null), CancellationToken.None);

        result.CurrentStatus.Should().Be("degraded");
        result.FailoverCount.Should().Be(1);
        result.RecoveryCount.Should().Be(0);
        result.Events.Should().ContainSingle(e => e.Direction == "FailedOverToFallback" && e.Reason == "stt timeout");
    }

    [Fact]
    public async Task Handle_ShouldReportHealthy_WhenTheMostRecentFailoverWasFollowedByARecovery()
    {
        var now = DateTime.UtcNow;
        _failoverEvents.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                MakeEvent(now.AddMinutes(-10), VoiceProviderFailoverDirection.FailedOverToFallback, "stt timeout"),
                MakeEvent(now.AddMinutes(-8), VoiceProviderFailoverDirection.RecoveredToPrimary),
            ]);

        var result = await _handler.Handle(new GetVoiceProviderHealthQuery(null, null), CancellationToken.None);

        result.CurrentStatus.Should().Be("healthy");
        result.FailoverCount.Should().Be(1);
        result.RecoveryCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ShouldReportHealthy_WhenNoEventsOccurredInTheWindow()
    {
        _failoverEvents.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.Handle(new GetVoiceProviderHealthQuery(null, null), CancellationToken.None);

        result.CurrentStatus.Should().Be("healthy");
        result.FailoverCount.Should().Be(0);
        result.RecoveryCount.Should().Be(0);
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldDefaultToLast24Hours_WhenNoRangeIsSpecified()
    {
        _failoverEvents.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _handler.Handle(new GetVoiceProviderHealthQuery(null, null), CancellationToken.None);

        await _failoverEvents.Received(1).GetEventsAsync(
            Arg.Is<DateTime>(from => (DateTime.UtcNow - from) > TimeSpan.FromHours(23.9) && (DateTime.UtcNow - from) < TimeSpan.FromHours(24.1)),
            Arg.Is<DateTime>(to => (DateTime.UtcNow - to) < TimeSpan.FromMinutes(1)),
            Arg.Any<CancellationToken>());
    }
}
