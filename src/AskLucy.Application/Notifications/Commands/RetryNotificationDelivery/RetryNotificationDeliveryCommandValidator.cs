using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.RetryNotificationDelivery;

public sealed class RetryNotificationDeliveryCommandValidator : AbstractValidator<RetryNotificationDeliveryCommand>
{
    public RetryNotificationDeliveryCommandValidator() => RuleFor(c => c.DeliveryId).NotEmpty();
}
