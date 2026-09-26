using MediatR;

namespace AskLucy.Application.OperationalFailures.Investigations.GetChatInvestigation;

/// <summary>specs/074 FR-016a–FR-016d — a read-only look at a chat an incident references.</summary>
public sealed record GetChatInvestigationQuery(Guid IncidentId, Guid ChatId) : IRequest<ChatInvestigationDto>;
