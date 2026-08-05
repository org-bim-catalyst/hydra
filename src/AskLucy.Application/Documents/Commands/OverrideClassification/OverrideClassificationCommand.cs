using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.OverrideClassification;

/// <summary>contracts/documents-api.md `PUT /api/v1/documents/{id}/classification` (FR-026).</summary>
public sealed record OverrideClassificationCommand(Guid DocumentId, Guid CategoryId) : IRequest<DocumentClassificationDto>;
