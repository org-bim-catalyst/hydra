using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.PublishAgentVersion;

/// <summary>Publishes an immutable snapshot of the current draft (spec.md FR-007-FR-010, contracts/agents-api.md).</summary>
public sealed record PublishAgentVersionCommand(Guid AgentId, string? ChangeDescription) : IRequest<AgentVersionDto>;
