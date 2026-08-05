using MediatR;

namespace AskLucy.Application.Documents.Commands.MoveDocument;

/// <summary>contracts/documents-api.md `PATCH /api/v1/documents/{id}/folder` — null moves to the root level (FR-033).</summary>
public sealed record MoveDocumentCommand(Guid DocumentId, Guid? FolderId) : IRequest<DocumentSummaryDto>;
