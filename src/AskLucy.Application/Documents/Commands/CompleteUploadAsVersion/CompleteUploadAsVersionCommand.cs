using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;

public enum VersionIncrement
{
    Major,
    Minor,
}

/// <summary>Resolves a checksum duplicate (FR-009) by linking the already-finalized upload as a new version of an existing document.</summary>
public sealed record CompleteUploadAsVersionCommand(Guid UploadSessionId, Guid ExistingDocumentId, VersionIncrement Increment) : IRequest<DocumentSummaryDto>;
