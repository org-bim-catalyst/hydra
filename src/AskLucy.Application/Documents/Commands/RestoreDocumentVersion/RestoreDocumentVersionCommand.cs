using MediatR;

namespace AskLucy.Application.Documents.Commands.RestoreDocumentVersion;

/// <summary>contracts/document-versions-folders-api.md `POST .../versions/{versionId}/actions/restore` (FR-041). Repoints `CurrentVersionId` without deleting any version row.</summary>
public sealed record RestoreDocumentVersionCommand(Guid DocumentId, Guid VersionId) : IRequest<DocumentSummaryDto>;
