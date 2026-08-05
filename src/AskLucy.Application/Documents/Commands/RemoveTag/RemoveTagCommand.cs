using MediatR;

namespace AskLucy.Application.Documents.Commands.RemoveTag;

/// <summary>contracts/documents-api.md `DELETE /api/v1/documents/{id}/tags/{tagName}` (FR-032).</summary>
public sealed record RemoveTagCommand(Guid DocumentId, string Name) : IRequest;
