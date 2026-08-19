using AskLucy.Domain.Panels;
using FluentValidation;

namespace AskLucy.Application.Panels.Commands.SaveUserPanelPreference;

/// <summary>Clarifications Q4 — a bounded range with a readability floor
/// (<see cref="UserPanelPreference.MinOpacityPercent"/>–<see cref="UserPanelPreference.MaxOpacityPercent"/>),
/// rejected outright rather than silently clamped at this boundary (constitution §2.VIII); the
/// domain-layer clamp in <see cref="UserPanelPreference.SetOpacityPercent"/> is defense-in-depth
/// for any future internal caller, not the user-facing contract.</summary>
public sealed class SaveUserPanelPreferenceCommandValidator : AbstractValidator<SaveUserPanelPreferenceCommand>
{
    public SaveUserPanelPreferenceCommandValidator()
    {
        RuleFor(c => c.OpacityPercent)
            .InclusiveBetween(UserPanelPreference.MinOpacityPercent, UserPanelPreference.MaxOpacityPercent)
            .WithMessage($"opacityPercent must be between {UserPanelPreference.MinOpacityPercent} and {UserPanelPreference.MaxOpacityPercent}.");
    }
}
