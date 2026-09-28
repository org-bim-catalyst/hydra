using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.MarkAllNotificationsRead;

public sealed class MarkAllNotificationsReadCommandValidator : AbstractValidator<MarkAllNotificationsReadCommand>
{
    public MarkAllNotificationsReadCommandValidator() =>
        RuleFor(c => c.Category!.Value).IsInEnum().When(c => c.Category is not null);
}
