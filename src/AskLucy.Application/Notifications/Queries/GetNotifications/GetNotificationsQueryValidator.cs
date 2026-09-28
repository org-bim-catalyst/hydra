using FluentValidation;

namespace AskLucy.Application.Notifications.Queries.GetNotifications;

public sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 100);
        RuleFor(q => q.State).IsInEnum();
        RuleForEach(q => q.Categories).IsInEnum();
    }
}
