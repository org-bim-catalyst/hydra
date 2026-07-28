using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetExternalLogins;

public sealed record GetExternalLoginsQuery(string UserId) : IRequest<IReadOnlyList<ExternalLoginDto>>;
