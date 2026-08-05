using MediatR;

namespace AskLucy.Application.Documents.Commands.DuplicateDocument;

/// <summary>contracts/documents-api.md `POST /api/v1/documents/{id}/actions/duplicate` — `201 Created` with the new document (FR-034).</summary>
public sealed record DuplicateDocumentCommand(Guid DocumentId) : IRequest<DocumentSummaryDto>;
