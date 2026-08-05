using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RenameDocument;

public sealed record RenameDocumentCommand(Guid DocumentId, string FileName) : IRequest<DocumentSummaryDto>;
