using AskLucy.Application.Agents;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.StartAgentExecution;

/// <summary>
/// Starts a new execution (spec.md FR-011, FR-051/FR-052, contracts/agents-api.md). Never
/// finishes synchronously — the caller gets back a summary immediately; the run continues in the
/// background (FR-017) via <c>IAgentExecutionRunner</c>.
/// </summary>
public sealed record StartAgentExecutionCommand(
    Guid AgentId,
    int? AgentVersionNumber,
    string Objective,
    AgentConversationIntegrationMode ConversationIntegrationMode,
    Guid? UserChatId,
    bool IsTestExecution) : IRequest<AgentExecutionSummaryDto>;
