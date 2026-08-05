using MediatR;

namespace AskLucy.Application.Documents.Commands.DeleteDocument;

public sealed record DeleteDocumentCommand(Guid DocumentId) : IRequest;
