using AskLucy.Domain.Authentication;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Authentication;

public sealed class PasswordResetTokenTests
{
    private const string UserId = "user-1";
    private const string Email = "user@example.com";
    private static readonly string Hash = new('a', 64);

    private static PasswordResetToken Issue(TimeSpan? lifetime = null) =>
        PasswordResetToken.IssueNew(UserId, Hash, Email, lifetime ?? TimeSpan.FromHours(1), "203.0.113.5");

    [Fact]
    public void IssueNew_ShouldStartRedeemable()
    {
        var token = Issue();

        token.IsRedeemable.Should().BeTrue();
        token.ConsumedAtUtc.Should().BeNull();
        token.SupersededAtUtc.Should().BeNull();
        token.ExpiresAtUtc.Should().BeAfter(token.CreatedAtUtc);
    }

    [Fact]
    public void IssueNew_ShouldThrow_WhenHashIsNotA64CharacterHexDigest()
    {
        var act = () => PasswordResetToken.IssueNew(UserId, "not-a-digest", Email, TimeSpan.FromHours(1));

        act.Should().Throw<ArgumentException>().WithParameterName("tokenHash");
    }

    [Fact]
    public void IssueNew_ShouldThrow_WhenEmailIsBlank()
    {
        var act = () => PasswordResetToken.IssueNew(UserId, Hash, "  ", TimeSpan.FromHours(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Consume_ShouldMakeTokenUnredeemable()
    {
        var token = Issue();

        token.Consume();

        token.IsRedeemable.Should().BeFalse();
        token.ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Consume_ShouldBeIdempotent_SoADoubleSubmitCannotResetTwice()
    {
        var token = Issue();
        token.Consume();
        var firstConsumedAt = token.ConsumedAtUtc;

        token.Consume();

        token.ConsumedAtUtc.Should().Be(firstConsumedAt);
    }

    [Fact]
    public void Supersede_ShouldMakeTokenUnredeemable()
    {
        var token = Issue();

        token.Supersede();

        token.IsRedeemable.Should().BeFalse();
        token.SupersededAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Supersede_ShouldBeIdempotent()
    {
        var token = Issue();
        token.Supersede();
        var firstSupersededAt = token.SupersededAtUtc;

        token.Supersede();

        token.SupersededAtUtc.Should().Be(firstSupersededAt);
    }

    [Fact]
    public void IsRedeemable_ShouldBeFalse_WhenExpired()
    {
        // A negative lifetime yields an already-expired token, so expiry is testable without a
        // clock abstraction or an hour-long test.
        var token = Issue(TimeSpan.FromHours(-1));

        token.IsRedeemable.Should().BeFalse();
    }

    [Fact]
    public void CanRedeemFor_ShouldBeTrue_ForTheIssuingEmail_IgnoringCase()
    {
        var token = Issue();

        token.CanRedeemFor("USER@EXAMPLE.COM").Should().BeTrue();
    }

    [Fact]
    public void CanRedeemFor_ShouldBeFalse_WhenTheAccountEmailHasChangedSinceIssue()
    {
        var token = Issue();

        token.CanRedeemFor("moved@example.com").Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void CanRedeemFor_ShouldBeFalse_ForEveryTerminalState(bool consumed, bool superseded, bool expired)
    {
        var token = Issue(expired ? TimeSpan.FromHours(-1) : TimeSpan.FromHours(1));

        if (consumed)
        {
            token.Consume();
        }

        if (superseded)
        {
            token.Supersede();
        }

        token.CanRedeemFor(Email).Should().BeFalse();
    }
}
