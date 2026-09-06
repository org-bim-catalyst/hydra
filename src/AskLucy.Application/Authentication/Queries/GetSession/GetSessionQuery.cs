using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetSession;

public sealed record GetSessionQuery(string RefreshToken) : IRequest<SessionResult>;

public sealed record SessionResult(bool Authenticated, string? UserId, IReadOnlyList<string> Roles)
{
    public static readonly SessionResult Anonymous = new(false, null, []);
}
