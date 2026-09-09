using MediatR;

namespace AskLucy.Application.Agents.Queries.GetSystemAgents;

/// <summary>specs/047 FR-001 — every system-owned agent, for the admin-only System Agents view. Never scoped to a caller's own agents; access control is the controller's job (AdministratorOrSuperUser policy).</summary>
public sealed record GetSystemAgentsQuery : IRequest<IReadOnlyList<AdminSystemAgentDto>>;
