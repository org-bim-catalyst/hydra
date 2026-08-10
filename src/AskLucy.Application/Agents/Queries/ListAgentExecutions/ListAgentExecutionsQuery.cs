using AskLucy.Application.Agents;
using AskLucy.Application.Common;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentExecutions;

public sealed record ListAgentExecutionsQuery(
    Guid? AgentId, AgentExecutionStatus? Status, bool? IsTestExecution, string? Cursor, int PageSize) : IRequest<PagedResult<AgentExecutionSummaryDto>>;
