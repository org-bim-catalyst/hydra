using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Queries.ListRelatedIncidents;

public sealed class ListRelatedIncidentsQueryValidator : AbstractValidator<ListRelatedIncidentsQuery>
{
    public ListRelatedIncidentsQueryValidator()
    {
        RuleFor(q => q.IncidentId).NotEmpty();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
