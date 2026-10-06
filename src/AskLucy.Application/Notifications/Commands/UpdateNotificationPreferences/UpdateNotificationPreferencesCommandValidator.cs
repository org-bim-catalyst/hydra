using AskLucy.Domain.Notifications;
using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.UpdateNotificationPreferences;

/// <summary>
/// The shape of the request. Whether a pair is mandatory is a different kind of failure (422, not 400),
/// decided against <see cref="NotificationTypeCatalog"/> in the handler.
/// </summary>
public sealed class UpdateNotificationPreferencesCommandValidator : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    public const int MaxChanges = 40;

    public UpdateNotificationPreferencesCommandValidator()
    {
        RuleFor(c => c.Changes)
            .NotNull()
            .Must(changes => changes is { Count: >= 1 and <= MaxChanges })
            .WithMessage($"Send between 1 and {MaxChanges} changes.")
            .Must(changes => changes is null || changes.Select(c => (c.Category, c.Channel)).Distinct().Count() == changes.Count)
            .WithMessage("Each category and channel can appear only once.");

        RuleForEach(c => c.Changes).ChildRules(change =>
        {
            change.RuleFor(c => c.Category).IsInEnum();
            change.RuleFor(c => c.Channel).IsInEnum();
            change.RuleFor(c => c.Frequency)
                .Must(f => f is null or DeliveryFrequency.Immediate)
                .WithMessage("Only immediate delivery is available.");
        }).When(c => c.Changes is not null);
    }
}
