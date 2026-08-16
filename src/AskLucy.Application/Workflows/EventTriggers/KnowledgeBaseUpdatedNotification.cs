using MediatR;

namespace AskLucy.Application.Workflows.EventTriggers;

/// <summary>Published from <c>UpdateKnowledgeBaseDetailsCommandHandler</c>, immediately after its own commit succeeds (research.md Decision 12).</summary>
public sealed record KnowledgeBaseUpdatedNotification(Guid KnowledgeBaseId, string OwnerId) : INotification;
