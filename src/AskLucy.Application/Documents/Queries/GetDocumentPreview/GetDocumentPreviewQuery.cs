using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentPreview;

public sealed record GetDocumentPreviewQuery(Guid DocumentId) : IRequest<DocumentPreviewResultDto>;
