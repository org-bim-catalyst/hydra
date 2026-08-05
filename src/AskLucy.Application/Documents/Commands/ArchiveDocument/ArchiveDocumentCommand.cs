using MediatR;

namespace AskLucy.Application.Documents.Commands.ArchiveDocument;

public sealed record ArchiveDocumentCommand(Guid DocumentId) : IRequest;
