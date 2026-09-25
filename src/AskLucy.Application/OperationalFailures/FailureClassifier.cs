using System.Net.Sockets;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>Maps an exception to the failure kind recorded on the admin trail (specs/074 research D4).</summary>
public interface IFailureClassifier
{
    /// <summary>The kind, or <see langword="null"/> when the caller's own token cancelled the work and nothing should be recorded (FR-006a).</summary>
    OperationalFailureKind? Classify(Exception exception, CancellationToken callerToken);

    /// <summary>The exception's type name — never its message, which may carry vendor text.</summary>
    string FallbackReason(Exception exception);
}

/// <inheritdoc />
public sealed class FailureClassifier : IFailureClassifier
{
    public OperationalFailureKind? Classify(Exception exception, CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return exception switch
        {
            AiProviderException provider => OperationalFailureKinds.FromProvider(provider.Kind),
            OperationCanceledException when callerToken.IsCancellationRequested => null,
            OperationCanceledException or TimeoutException => OperationalFailureKind.TimedOut,
            HttpRequestException or SocketException => OperationalFailureKind.DependencyUnreachable,
            _ => OperationalFailureKind.UnexpectedError,
        };
    }

    public string FallbackReason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetType().Name;
    }
}
