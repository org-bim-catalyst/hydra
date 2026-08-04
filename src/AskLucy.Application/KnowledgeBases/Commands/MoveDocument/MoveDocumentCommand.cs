using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.MoveDocument;

/// <summary><paramref name="NewFolderId"/> null moves the document to the knowledge base's root.</summary>
public sealed record MoveDocumentCommand(Guid KnowledgeBaseId, Guid DocumentId, Guid? NewFolderId) : IRequest<KnowledgeBaseDocumentDto>;
