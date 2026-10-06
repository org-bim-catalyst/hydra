using AskLucy.Domain.Notifications;
using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.PublishSystemAnnouncement;

public sealed class PublishSystemAnnouncementCommandValidator : AbstractValidator<PublishSystemAnnouncementCommand>
{
    public PublishSystemAnnouncementCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Audience).IsInEnum();

        RuleFor(c => c.Title).NotEmpty().MaximumLength(SystemAnnouncement.TitleMaxLength)
            .Must(t => !TemplateTokenParser.ContainsRawUrlOrHtml(t)).WithMessage("Links and HTML aren't allowed in an announcement.");
        RuleFor(c => c.Message).NotEmpty().MaximumLength(SystemAnnouncement.MessageMaxLength)
            .Must(m => !TemplateTokenParser.ContainsRawUrlOrHtml(m)).WithMessage("Links and HTML aren't allowed in an announcement.");

        RuleFor(c => c.TargetRoleIds)
            .Must(ids => ids is { Count: > 0 }).When(c => c.Audience == AnnouncementAudience.Roles)
            .WithMessage("Choose at least one role for a role-targeted announcement.");
        RuleFor(c => c.TargetRoleIds)
            .Must(ids => ids is null or { Count: 0 }).When(c => c.Audience == AnnouncementAudience.AllActiveUsers)
            .WithMessage("An announcement to all active users can't also target roles.");

        RuleFor(c => c.EndsAtUtc)
            .Must(ends => ends!.Value > timeProvider.GetUtcNow().UtcDateTime).When(c => c.EndsAtUtc is not null)
            .WithMessage("The announcement end time must be in the future.");
    }
}
