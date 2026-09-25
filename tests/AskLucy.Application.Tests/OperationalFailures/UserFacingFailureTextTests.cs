using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 research D9 — two calm sentences, and neither names a cause (SC-001).</summary>
public sealed class UserFacingFailureTextTests
{
    /// <summary>SC-001: no credential, key, quota, rate limit, billing condition, provider, model or "an administrator".</summary>
    public static readonly string[] Sc001Words = ["credential", "key", "quota", "rate limit", "billing", "provider", "model", "administrator", "401", "403"];

    [Theory]
    [InlineData(OperationalFailureKind.NotConfigured)]
    [InlineData(OperationalFailureKind.CredentialRejected)]
    [InlineData(OperationalFailureKind.CredentialUnreadable)]
    [InlineData(OperationalFailureKind.QuotaExhausted)]
    [InlineData(OperationalFailureKind.UsageRestricted)]
    public void For_ShouldSayLater_WhenRetryingCannotHelp(OperationalFailureKind kind)
    {
        UserFacingFailureText.For(kind).Should().Be(UserFacingFailureText.Later);
    }

    [Theory]
    [InlineData(OperationalFailureKind.RateLimited)]
    [InlineData(OperationalFailureKind.Unavailable)]
    [InlineData(OperationalFailureKind.UnexpectedError)]
    [InlineData(OperationalFailureKind.TimedOut)]
    [InlineData(null)]
    public void For_ShouldSayRetry_Otherwise(OperationalFailureKind? kind)
    {
        UserFacingFailureText.For(kind).Should().Be(UserFacingFailureText.Retry);
    }

    [Fact]
    public void NeitherSentence_ShouldNameACause()
    {
        foreach (var sentence in new[] { UserFacingFailureText.Retry, UserFacingFailureText.Later })
        {
            foreach (var word in Sc001Words)
            {
                sentence.Should().NotContainEquivalentOf(word);
            }
        }
    }
}
