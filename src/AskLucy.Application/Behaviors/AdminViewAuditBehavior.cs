using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Behaviors;

internal static partial class AdminViewAuditBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Auditing {Action} of {TargetType} {TargetId} by {UserId} failed; the view is refused.")]
    public static partial void AuditFailed(ILogger logger, Exception exception, NotificationAuditAction action, string targetType, string targetId, string? userId);
}

/// <summary>
/// Writes the <c>…Viewed</c> audit row for an <see cref="IAuditedAdminView"/> request after its
/// handler succeeds, at most once per admin, per resource, per hour (specs/067 T230). The row is
/// saved in its own scope, so it never rides on, or is lost with, the query's unit of work. If the
/// write fails the request fails too: an unaudited view of delivery data is not returned.
/// </summary>
public sealed class AdminViewAuditBehavior<TRequest, TResponse>(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<AdminViewAuditBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public static readonly TimeSpan AuditWindow = TimeSpan.FromHours(1);

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);
        if (request is not IAuditedAdminView view)
        {
            return response;
        }

        string? userId = null;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            userId = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>().UserId
                ?? throw new InvalidOperationException($"{typeof(TRequest).Name} is an audited admin view but has no acting user.");

            var cacheKey = $"notifications:view-audit:{userId}:{view.AuditAction}:{view.AuditTargetType}:{view.AuditTargetId}";
            if (cache.TryGetValue(cacheKey, out _))
            {
                return response;
            }

            // A miss can be a restart or another instance, so the trail itself has the final say.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var auditLogs = scope.ServiceProvider.GetRequiredService<INotificationAuditLogRepository>();
            if (!await auditLogs.ExistsSinceAsync(view.AuditAction, userId, view.AuditTargetType, view.AuditTargetId, now - AuditWindow, cancellationToken))
            {
                scope.ServiceProvider.GetRequiredService<INotificationAuditWriter>()
                    .Write(view.AuditAction, view.AuditTargetType, view.AuditTargetId, NotificationAuditOutcome.Succeeded);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            }

            cache.Set(cacheKey, true, AuditWindow);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminViewAuditBehaviorLog.AuditFailed(logger, ex, view.AuditAction, view.AuditTargetType, view.AuditTargetId, userId);
            throw;
        }
    }
}
