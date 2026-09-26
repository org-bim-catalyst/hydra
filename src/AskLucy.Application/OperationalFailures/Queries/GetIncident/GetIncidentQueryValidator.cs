using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Queries.GetIncident;

public sealed class GetIncidentQueryValidator : AbstractValidator<GetIncidentQuery>
{
    public GetIncidentQueryValidator()
    {
        RuleFor(q => q.IncidentId).NotEmpty();
    }
}
