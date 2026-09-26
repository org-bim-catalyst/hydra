using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetAdministratorContentAccess;

public sealed record GetAdministratorContentAccessQuery : IRequest<AdministratorContentAccessDto>;
