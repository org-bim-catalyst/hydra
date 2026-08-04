using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteDocument;

public sealed record DeleteDocumentCommand(Guid KnowledgeBaseId, Guid DocumentId) : IRequest;
