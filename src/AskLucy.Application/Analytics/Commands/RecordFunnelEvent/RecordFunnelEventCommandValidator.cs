using FluentValidation;

namespace AskLucy.Application.Analytics.Commands.RecordFunnelEvent;

/// <summary>
/// Closed-enum + companion-field validation (data-model.md validation rules; constitution
/// §8 threat model — this is an anonymous, public endpoint, so every field is constrained
/// rather than trusted). A bounded clock-skew/staleness window on <c>OccurredAtUtc</c>
/// rejects replay/garbage timestamps without requiring any server-side session state.
/// </summary>
public sealed class RecordFunnelEventCommandValidator : AbstractValidator<RecordFunnelEventCommand>
{
    public RecordFunnelEventCommandValidator()
    {
        RuleFor(c => c.EventType).IsInEnum();
        RuleFor(c => c.SessionId).NotEmpty();

        RuleFor(c => c.CtaId)
            .NotNull()
            .WithMessage("CtaId is required when EventType is CtaClicked.")
            .When(c => c.EventType == FunnelEventType.CtaClicked);
        RuleFor(c => c.CtaId)
            .Null()
            .WithMessage("CtaId must not be set when EventType is FunnelCompleted.")
            .When(c => c.EventType == FunnelEventType.FunnelCompleted);
        RuleFor(c => c.CtaId!.Value).IsInEnum().When(c => c.CtaId.HasValue);

        RuleFor(c => c.FunnelType)
            .NotNull()
            .WithMessage("FunnelType is required when EventType is FunnelCompleted.")
            .When(c => c.EventType == FunnelEventType.FunnelCompleted);
        RuleFor(c => c.FunnelType)
            .Null()
            .WithMessage("FunnelType must not be set when EventType is CtaClicked.")
            .When(c => c.EventType == FunnelEventType.CtaClicked);
        RuleFor(c => c.FunnelType!.Value).IsInEnum().When(c => c.FunnelType.HasValue);

        RuleFor(c => c.OccurredAtUtc)
            .Must(t => t <= DateTime.UtcNow.AddMinutes(2))
            .WithMessage("OccurredAtUtc must not be more than 2 minutes in the future.")
            .Must(t => t >= DateTime.UtcNow.AddHours(-1))
            .WithMessage("OccurredAtUtc must not be more than 1 hour in the past.");
    }
}
