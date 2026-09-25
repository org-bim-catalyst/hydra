using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.OperationalFailures;

/// <summary>Every FR-009 row (research D5).</summary>
public sealed class OperationalFailureSeverityPolicyTests
{
    public static TheoryData<OperationalFailureKind> CriticalKinds =>
    [
        OperationalFailureKind.CredentialRejected,
        OperationalFailureKind.CredentialUnreadable,
        OperationalFailureKind.NotConfigured,
        OperationalFailureKind.QuotaExhausted,
        OperationalFailureKind.UsageRestricted,
    ];

    [Theory]
    [MemberData(nameof(CriticalKinds))]
    public void CriticalKind_IsCritical_EvenWhenAFallbackServed(OperationalFailureKind kind)
    {
        foreach (var outcome in Enum.GetValues<OperationalFailureOutcome>())
        {
            OperationalFailureSeverityPolicy.Classify(OperationalFailureEngine.Voice, kind, outcome)
                .Should().Be(OperationalFailureSeverity.Critical, $"{kind} with {outcome}");
        }
    }

    [Fact]
    public void AccessEngine_IsAlwaysWarning()
    {
        foreach (var kind in Enum.GetValues<OperationalFailureKind>())
        {
            foreach (var outcome in Enum.GetValues<OperationalFailureOutcome>())
            {
                OperationalFailureSeverityPolicy.Classify(OperationalFailureEngine.Access, kind, outcome)
                    .Should().Be(OperationalFailureSeverity.Warning, $"{kind} with {outcome}");
            }
        }
    }

    [Fact]
    public void RateLimited_IsWarning()
    {
        OperationalFailureSeverityPolicy.Classify(OperationalFailureEngine.Chat, OperationalFailureKind.RateLimited, OperationalFailureOutcome.Failed)
            .Should().Be(OperationalFailureSeverity.Warning);
    }

    [Theory]
    [InlineData(OperationalFailureOutcome.DegradedServed)]
    [InlineData(OperationalFailureOutcome.RecoveredByRetry)]
    public void NonCriticalKind_ServedAnyway_IsWarning(OperationalFailureOutcome outcome)
    {
        OperationalFailureSeverityPolicy.Classify(OperationalFailureEngine.Voice, OperationalFailureKind.Unavailable, outcome)
            .Should().Be(OperationalFailureSeverity.Warning);
    }

    [Theory]
    [InlineData(OperationalFailureKind.Unavailable)]
    [InlineData(OperationalFailureKind.RequestInvalid)]
    [InlineData(OperationalFailureKind.ResponseNotUnderstood)]
    [InlineData(OperationalFailureKind.UnexpectedError)]
    [InlineData(OperationalFailureKind.TimedOut)]
    [InlineData(OperationalFailureKind.DependencyUnreachable)]
    [InlineData(OperationalFailureKind.ValidationFailed)]
    [InlineData(OperationalFailureKind.JobFailedAfterRetries)]
    public void OtherFailedKinds_AreError(OperationalFailureKind kind)
    {
        foreach (var engine in Enum.GetValues<OperationalFailureEngine>().Where(e => e != OperationalFailureEngine.Access))
        {
            OperationalFailureSeverityPolicy.Classify(engine, kind, OperationalFailureOutcome.Failed)
                .Should().Be(OperationalFailureSeverity.Error, $"{engine}/{kind}");
        }
    }
}
