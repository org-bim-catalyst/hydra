using FluentValidation;

namespace AskLucy.Application.Common;

public sealed record BulkTarget(IReadOnlyList<string>? Ids, bool AllMatching);

public sealed class BulkTargetValidator : AbstractValidator<BulkTarget>
{
    public BulkTargetValidator()
    {
        RuleFor(t => t).Must(t => (t.Ids is { Count: > 0 }) ^ t.AllMatching)
            .WithMessage("Exactly one of Ids (non-empty) or AllMatching must be set.");
    }
}
