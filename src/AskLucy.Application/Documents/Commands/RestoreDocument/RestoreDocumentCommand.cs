using MediatR;

namespace AskLucy.Application.Documents.Commands.RestoreDocument;

/// <summary>Restores a document from either the Archived or Deleted view (FR-016, FR-017) — whichever applies.</summary>
public sealed record RestoreDocumentCommand(Guid DocumentId) : IRequest;
