using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentProcessingStatus;

public sealed record GetDocumentProcessingStatusQuery(Guid DocumentId) : IRequest<DocumentProcessingStatusDto>;
