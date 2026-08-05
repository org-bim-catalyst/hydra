using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.UpdateDocumentMetadata;

/// <summary>contracts/documents-api.md `PATCH /api/v1/documents/{id}/metadata` — only supplied fields change (FR-031).</summary>
public sealed record UpdateDocumentMetadataCommand(
    Guid DocumentId,
    byte[] RowVersion,
    string? Title,
    string? Author,
    DateTime? CreationDate,
    DateTime? ModificationDate,
    string? Keywords) : IRequest<UpdateDocumentMetadataResult>;

public sealed record UpdateDocumentMetadataResult(DocumentMetadataDto Metadata, bool WasStale);
