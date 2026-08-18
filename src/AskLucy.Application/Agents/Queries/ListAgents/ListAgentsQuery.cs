using AskLucy.Application.Agents;
using AskLucy.Application.Common;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgents;

public sealed record ListAgentsQuery(AgentStatus? Status, AgentType? AgentType, string? Cursor, int PageSize) : IRequest<PagedResult<AgentListItemDto>>;
