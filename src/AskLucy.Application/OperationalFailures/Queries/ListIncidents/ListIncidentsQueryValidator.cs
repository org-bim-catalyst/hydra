using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Queries.ListIncidents;

public sealed class ListIncidentsQueryValidator : AbstractValidator<ListIncidentsQuery>
{
    public ListIncidentsQueryValidator()
    {
        RuleFor(q => q.FromUtc)
            .LessThanOrEqualTo(q => q.ToUtc)
            .When(q => q.FromUtc is not null && q.ToUtc is not null)
            .WithMessage("from must not be later than to.");
        RuleFor(q => q.State).IsInEnum();
        RuleFor(q => q.Severity).IsInEnum();
        RuleFor(q => q.Engine).IsInEnum();
        RuleFor(q => q.Kind).IsInEnum();
        RuleFor(q => q.Provider).MaximumLength(100);
        RuleFor(q => q.UserId).MaximumLength(450);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
