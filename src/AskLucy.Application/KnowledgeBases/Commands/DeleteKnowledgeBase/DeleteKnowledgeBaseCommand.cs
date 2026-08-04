using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;

/// <summary>Soft-deletes a knowledge base (FR-005), scheduling the automatic 30-day purge (FR-036). No confirmation required — reversible via Restore, mirrors <c>DeleteUserChatCommand</c>.</summary>
public sealed record DeleteKnowledgeBaseCommand(Guid Id) : IRequest;
