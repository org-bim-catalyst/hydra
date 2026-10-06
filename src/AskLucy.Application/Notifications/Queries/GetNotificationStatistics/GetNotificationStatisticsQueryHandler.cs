using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationStatistics;

public sealed class GetNotificationStatisticsQueryHandler(INotificationAdminRepository repository, TimeProvider timeProvider)
    : IRequestHandler<GetNotificationStatisticsQuery, NotificationStatisticsDto>
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);
    private static readonly TimeSpan HourlyUpTo = TimeSpan.FromDays(2);

    public async Task<NotificationStatisticsDto> Handle(GetNotificationStatisticsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var to = request.ToUtc ?? now;
        var from = request.FromUtc ?? to - DefaultRange;

        // The validator checks an explicit pair; a lone 'from' with the default 'to' is checked here.
        if (to - from > TimeSpan.FromDays(GetNotificationStatisticsQueryValidator.MaxRangeDays) || from >= to)
        {
            throw new FluentValidation.ValidationException(
                [new FluentValidation.Results.ValidationFailure("from", "The range must be positive and at most 90 days.")]);
        }

        var data = await repository.GetStatisticsAsync(from, to, hourlyBuckets: to - from <= HourlyUpTo, now, cancellationToken);

        var terminalEmail = data.EmailSent + data.EmailFailed;
        return new NotificationStatisticsDto(
            data.Created,
            data.Sent,
            data.Failed,
            data.DeadLettered,
            data.Ambiguous,
            terminalEmail == 0 ? null : Math.Round((double)data.EmailSent / terminalEmail, 3),
            data.AverageLatencyMs,
            data.P95LatencyMs,
            data.Retries,
            new NotificationBacklogDto(
                data.OutboxPending,
                data.DeliveriesDue,
                data.OldestDueAtUtc is { } oldest ? (int)Math.Max(0, (now - oldest).TotalSeconds) : 0),
            data.UnreadNotifications,
            [.. data.ByCategory.Select(c => new NotificationCategoryStatDto(c.Category, c.Created, c.Failed))],
            [.. data.Series.Select(s => new NotificationSeriesBucketDto(s.BucketStartUtc, s.Created, s.Sent, s.Failed))]);
    }
}
