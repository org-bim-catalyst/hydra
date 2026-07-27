using MediatR;

namespace AskLucy.Application.Users.Queries.GetAllUsers;

public sealed record GetAllUsersQuery : IRequest<IReadOnlyList<UserAdminDto>>;
