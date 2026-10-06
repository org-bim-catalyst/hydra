using AskLucy.Application.Options;
using AskLucy.Infrastructure.Notifications.Email;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// T104 — <see cref="EmailSendRateLimiter"/> (research R6, SC-014): the token bucket holds
/// <c>MaxPerMinute</c> sends per minute, and the reserved lane stays available to mandatory mail however
/// much optional traffic has drained the rest.
/// </summary>
public sealed class EmailSendRateLimiterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));

    private EmailSendRateLimiter CreateSut(int maxPerMinute, int reserved)
    {
        var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
        monitor.CurrentValue.Returns(new NotificationsOptions
        {
            Email = new NotificationEmailOptions { MaxPerMinute = maxPerMinute, ReservedPerMinuteForMandatory = reserved },
        });
        return new EmailSendRateLimiter(monitor, _time);
    }

    private static int Drain(EmailSendRateLimiter limiter, bool mandatory, int attempts)
    {
        var granted = 0;
        for (var i = 0; i < attempts; i++)
        {
            if (limiter.TryAcquire(mandatory, out _))
            {
                granted++;
            }
        }

        return granted;
    }

    [Fact]
    public void TryAcquire_NeverGrantsMoreThanMaxPerMinute_InOneMinute()
    {
        var limiter = CreateSut(maxPerMinute: 10, reserved: 4);

        // Mandatory mail may use both lanes, so it is the bound on the total.
        Drain(limiter, mandatory: true, attempts: 100).Should().Be(10);
    }

    [Fact]
    public void TryAcquire_OptionalMail_CanUseOnlyTheGeneralLane()
    {
        var limiter = CreateSut(maxPerMinute: 10, reserved: 4);

        Drain(limiter, mandatory: false, attempts: 100).Should().Be(6);
    }

    [Fact]
    public void TryAcquire_ReservedLane_IsStillAvailableToMandatoryMail_AfterOptionalTrafficDrainedTheBucket()
    {
        var limiter = CreateSut(maxPerMinute: 10, reserved: 4);
        Drain(limiter, mandatory: false, attempts: 100);

        limiter.TryAcquire(mandatory: false, out _).Should().BeFalse();

        Drain(limiter, mandatory: true, attempts: 10).Should().Be(4);
    }

    [Fact]
    public void TryAcquire_WhenEmpty_SaysHowLongUntilTheNextToken()
    {
        var limiter = CreateSut(maxPerMinute: 60, reserved: 0);
        Drain(limiter, mandatory: false, attempts: 100);

        limiter.TryAcquire(mandatory: false, out var retryAfter).Should().BeFalse();

        // 60 per minute is one token a second.
        retryAfter.Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void TryAcquire_RefillsOverTime()
    {
        var limiter = CreateSut(maxPerMinute: 60, reserved: 0);
        Drain(limiter, mandatory: false, attempts: 100);

        _time.Advance(TimeSpan.FromSeconds(10));

        Drain(limiter, mandatory: false, attempts: 100).Should().Be(10);
    }

    [Fact]
    public void TryAcquire_NeverHoldsMoreThanOneMinutesWorth_EvenAfterALongIdleSpell()
    {
        var limiter = CreateSut(maxPerMinute: 10, reserved: 0);

        _time.Advance(TimeSpan.FromHours(3));

        Drain(limiter, mandatory: false, attempts: 100).Should().Be(10);
    }

    [Fact]
    public void TryAcquire_ReservedLaneAtOrAboveTheTotal_StillLeavesOneGeneralTokenSoOptionalMailIsNotSilenced()
    {
        var limiter = CreateSut(maxPerMinute: 5, reserved: 20);

        Drain(limiter, mandatory: false, attempts: 100).Should().Be(1);
    }
}
