using FluentValidation;

namespace AskLucy.Application.Notifications.Queries.GetSystemAnnouncements;

public sealed class GetSystemAnnouncementsQueryValidator : AbstractValidator<GetSystemAnnouncementsQuery>
{
    public GetSystemAnnouncementsQueryValidator() => RuleFor(q => q.Limit).InclusiveBetween(1, GetSystemAnnouncementsQuery.MaxLimit);
}
