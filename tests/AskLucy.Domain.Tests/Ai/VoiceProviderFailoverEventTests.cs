using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

public sealed class VoiceProviderFailoverEventTests
{
    [Fact]
    public void Create_ShouldRecordDirectionAndReason()
    {
        var occurredAtUtc = DateTime.UtcNow;

        var failoverEvent = VoiceProviderFailoverEvent.Create(
            "user-1", occurredAtUtc, VoiceProviderFailoverDirection.FailedOverToFallback, "stt-session request timed out", "user-1");

        failoverEvent.UserId.Should().Be("user-1");
        failoverEvent.OccurredAtUtc.Should().Be(occurredAtUtc);
        failoverEvent.Direction.Should().Be(VoiceProviderFailoverDirection.FailedOverToFallback);
        failoverEvent.Reason.Should().Be("stt-session request timed out");
    }

    [Fact]
    public void Create_ShouldAllowNullReason_ForRecoveryEvents()
    {
        var failoverEvent = VoiceProviderFailoverEvent.Create(
            "user-1", DateTime.UtcNow, VoiceProviderFailoverDirection.RecoveredToPrimary, reason: null, "user-1");

        failoverEvent.Direction.Should().Be(VoiceProviderFailoverDirection.RecoveredToPrimary);
        failoverEvent.Reason.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenUserIdIsBlank(string blankUserId)
    {
        var act = () => VoiceProviderFailoverEvent.Create(
            blankUserId, DateTime.UtcNow, VoiceProviderFailoverDirection.FailedOverToFallback, "reason", "user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }
}
