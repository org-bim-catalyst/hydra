using FluentValidation;

namespace AskLucy.Application.Notifications.Queries.GetNotificationAudit;

public sealed class GetNotificationAuditQueryValidator : AbstractValidator<GetNotificationAuditQuery>
{
    public GetNotificationAuditQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, GetNotificationAuditQuery.MaxLimit);
        RuleFor(q => q.Action!.Value).IsInEnum().When(q => q.Action is not null);
        RuleFor(q => q.TargetType).MaximumLength(60);
        RuleFor(q => q.TargetId).MaximumLength(100);
        RuleFor(q => q.ActorUserId).MaximumLength(450);
        RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc <= q.ToUtc)
            .WithName("from").WithMessage("'from' must not be after 'to'.");
    }
}
