using MediatR;

namespace AskLucy.Application.Users.Queries.GetUsersEligibleIds;

public sealed record GetUsersEligibleIdsQuery(string? Search, UserBulkAction Action) : IRequest<IReadOnlyList<string>>;
