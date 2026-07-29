using MediatR;

namespace AskLucy.Application.Users.Queries.GetUsers;

/// <summary>Replaces <c>GetAllUsersQuery</c> — admin listing with search/sort/pagination (FR-009/010/011).</summary>
public sealed record GetUsersQuery(string? Search, string SortBy, bool SortDescending, int Page, int PageSize)
    : IRequest<PagedResult<UserAdminDto>>;
