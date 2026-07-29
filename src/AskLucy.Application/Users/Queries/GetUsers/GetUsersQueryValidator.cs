using FluentValidation;

namespace AskLucy.Application.Users.Queries.GetUsers;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.SortBy).Must(s => s is "email" or "createdAtUtc")
            .WithMessage("sortBy must be 'email' or 'createdAtUtc'.");
    }
}
