using FluentValidation;

namespace AskLucy.Application.Notifications.Queries.GetNotificationDeliveries;

public sealed class GetNotificationDeliveriesQueryValidator : AbstractValidator<GetNotificationDeliveriesQuery>
{
    public GetNotificationDeliveriesQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, GetNotificationDeliveriesQuery.MaxLimit);
        RuleForEach(q => q.Statuses).IsInEnum().When(q => q.Statuses is not null);
        RuleFor(q => q.Channel!.Value).IsInEnum().When(q => q.Channel is not null);
        RuleFor(q => q.Category!.Value).IsInEnum().When(q => q.Category is not null);
        RuleFor(q => q.Type).MaximumLength(100);
        RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc <= q.ToUtc)
            .WithName("from").WithMessage("'from' must not be after 'to'.");
    }
}
