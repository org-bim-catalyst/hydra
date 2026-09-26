using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Queries.ListOccurrences;

public sealed class ListOccurrencesQueryValidator : AbstractValidator<ListOccurrencesQuery>
{
    public ListOccurrencesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
