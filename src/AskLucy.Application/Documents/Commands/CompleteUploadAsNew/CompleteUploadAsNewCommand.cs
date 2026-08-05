using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUploadAsNew;

/// <summary>Resolves a checksum duplicate (FR-009) by proceeding as a separate new document anyway, rather than linking to the existing one.</summary>
public sealed record CompleteUploadAsNewCommand(Guid UploadSessionId) : IRequest<DocumentSummaryDto>;
