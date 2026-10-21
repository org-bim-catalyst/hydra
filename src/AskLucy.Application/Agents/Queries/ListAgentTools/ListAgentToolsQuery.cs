using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentTools;

/// <summary>
/// specs/083 — every tool the platform's agents can call, for the admin-only AI Tools view.
/// Read-only: the catalogue is a fact about the deployment, not a setting.
/// </summary>
public sealed record ListAgentToolsQuery : IRequest<IReadOnlyList<AgentToolDto>>;
