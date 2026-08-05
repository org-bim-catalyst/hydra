using MediatR;

namespace AskLucy.Application.Documents.Commands.AddTag;

/// <summary>contracts/documents-api.md `POST /api/v1/documents/{id}/tags` — creates the tag for this user if it doesn't already exist, then attaches it (FR-032).</summary>
public sealed record AddTagCommand(Guid DocumentId, string Name) : IRequest<IReadOnlyList<string>>;
