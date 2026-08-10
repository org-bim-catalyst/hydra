using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentApproval;

public sealed record GetAgentApprovalQuery(Guid AgentExecutionId, Guid ApprovalId) : IRequest<AgentApprovalDto>;
