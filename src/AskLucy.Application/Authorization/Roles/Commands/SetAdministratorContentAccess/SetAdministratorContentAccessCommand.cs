using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.SetAdministratorContentAccess;

/// <summary>specs/074 FR-016g — a Super User lets (or stops letting) every Administrator view user content.</summary>
public sealed record SetAdministratorContentAccessCommand(bool Granted) : IRequest<AdministratorContentAccessDto>;
