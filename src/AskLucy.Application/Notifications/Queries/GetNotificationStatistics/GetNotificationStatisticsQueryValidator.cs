using FluentValidation;

namespace AskLucy.Application.Notifications.Queries.GetNotificationStatistics;

public sealed class GetNotificationStatisticsQueryValidator : AbstractValidator<GetNotificationStatisticsQuery>
{
    public const int MaxRangeDays = 90;

    public GetNotificationStatisticsQueryValidator()
    {
        RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc < q.ToUtc)
            .WithName("from").WithMessage("'from' must be before 'to'.");
        RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.ToUtc.Value - q.FromUtc.Value <= TimeSpan.FromDays(MaxRangeDays))
            .WithName("to").WithMessage($"The range can't be longer than {MaxRangeDays} days.");
    }
}
