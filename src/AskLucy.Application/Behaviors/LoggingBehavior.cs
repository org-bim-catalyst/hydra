using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Behaviors;

internal static partial class LoggingBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    public static partial void Handling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName}")]
    public static partial void Handled(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed")]
    public static partial void Failed(ILogger logger, Exception exception, string requestName);
}

/// <summary>
/// Structured logging around every request (constitution &#167;4/&#167;14) — named properties,
/// never string-concatenated messages, and never logs the request payload itself (which
/// may contain user prompt content).
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        LoggingBehaviorLog.Handling(logger, requestName);

        try
        {
            var response = await next(cancellationToken);
            LoggingBehaviorLog.Handled(logger, requestName);
            return response;
        }
        catch (Exception ex)
        {
            LoggingBehaviorLog.Failed(logger, ex, requestName);
            throw;
        }
    }
}
