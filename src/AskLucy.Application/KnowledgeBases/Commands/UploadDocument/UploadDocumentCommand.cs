using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UploadDocument;

public sealed record UploadDocumentCommand(Guid KnowledgeBaseId, Guid? FolderId, Stream Content, string FileName, long SizeBytes)
    : IRequest<KnowledgeBaseDocumentDto>;
