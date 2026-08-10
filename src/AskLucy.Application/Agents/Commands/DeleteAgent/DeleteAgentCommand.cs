using MediatR;

namespace AskLucy.Application.Agents.Commands.DeleteAgent;

/// <summary>Soft delete only — never cascades to versions/executions (spec.md FR-050 audit trail requires they survive).</summary>
public sealed record DeleteAgentCommand(Guid Id) : IRequest;
