using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocument;

public sealed record GetDocumentQuery(Guid DocumentId) : IRequest<DocumentDetailDto>;
