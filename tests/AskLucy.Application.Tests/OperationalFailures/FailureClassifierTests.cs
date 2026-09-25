using System.Net.Sockets;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 research D4 — an exception maps to a failure kind, or to "do not record" when the caller cancelled.</summary>
public sealed class FailureClassifierTests
{
    private readonly FailureClassifier _classifier = new();

    public static TheoryData<Exception, OperationalFailureKind> ProviderExceptions => new()
    {
        { new AiProviderAuthenticationException("x"), OperationalFailureKind.CredentialRejected },
        { new AiProviderCredentialUnreadableException("x"), OperationalFailureKind.CredentialUnreadable },
        { new AiProviderNotConfiguredException("x"), OperationalFailureKind.NotConfigured },
        { new AiProviderQuotaExhaustedException("x"), OperationalFailureKind.QuotaExhausted },
        { new AiProviderRateLimitedException("x"), OperationalFailureKind.RateLimited },
        { new AiProviderUsageRestrictedException("x"), OperationalFailureKind.UsageRestricted },
        { new AiProviderUnavailableException("x"), OperationalFailureKind.Unavailable },
        { new AiProviderRequestInvalidException("x"), OperationalFailureKind.RequestInvalid },
        { new AiProviderResponseInvalidException("x"), OperationalFailureKind.ResponseNotUnderstood },
    };

    [Theory]
    [MemberData(nameof(ProviderExceptions))]
    public void Classify_MapsAProviderExceptionToItsKind(Exception exception, OperationalFailureKind expected)
    {
        _classifier.Classify(exception, CancellationToken.None).Should().Be(expected);
    }

    [Fact]
    public void Classify_MapsATimeoutToTimedOut()
    {
        _classifier.Classify(new TimeoutException(), CancellationToken.None).Should().Be(OperationalFailureKind.TimedOut);
    }

    [Fact]
    public void Classify_MapsACancellationTheCallerDidNotAskForToTimedOut()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException while the caller's token is still live.
        using var callerSource = new CancellationTokenSource();

        _classifier.Classify(new TaskCanceledException("timed out", new TimeoutException()), callerSource.Token)
            .Should().Be(OperationalFailureKind.TimedOut);
    }

    [Fact]
    public void Classify_ReturnsNullWhenTheCallerCancelled()
    {
        using var callerSource = new CancellationTokenSource();
        callerSource.Cancel();

        _classifier.Classify(new OperationCanceledException(callerSource.Token), callerSource.Token).Should().BeNull();
        _classifier.Classify(new TaskCanceledException(), callerSource.Token).Should().BeNull();
    }

    [Fact]
    public void Classify_MapsNetworkFailuresToDependencyUnreachable()
    {
        _classifier.Classify(new HttpRequestException("connect failed"), CancellationToken.None)
            .Should().Be(OperationalFailureKind.DependencyUnreachable);
        _classifier.Classify(new SocketException((int)SocketError.ConnectionRefused), CancellationToken.None)
            .Should().Be(OperationalFailureKind.DependencyUnreachable);
    }

    [Fact]
    public void Classify_MapsAnythingElseToUnexpectedError()
    {
        _classifier.Classify(new InvalidOperationException("boom"), CancellationToken.None)
            .Should().Be(OperationalFailureKind.UnexpectedError);
    }

    [Fact]
    public void Classify_UnwrapsASingleAggregateInnerException()
    {
        _classifier.Classify(new AggregateException(new AiProviderAuthenticationException("x")), CancellationToken.None)
            .Should().Be(OperationalFailureKind.CredentialRejected);
    }

    [Fact]
    public void FallbackReason_IsTheTypeNameNeverTheMessage()
    {
        var reason = _classifier.FallbackReason(new InvalidOperationException("Server=db;Password=hunter2"));

        reason.Should().Be(nameof(InvalidOperationException));
    }
}
