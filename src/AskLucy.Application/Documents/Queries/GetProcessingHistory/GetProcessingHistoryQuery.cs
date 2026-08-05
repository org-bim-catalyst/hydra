using MediatR;

namespace AskLucy.Application.Documents.Queries.GetProcessingHistory;

public sealed record GetProcessingHistoryQuery(Guid DocumentId) : IRequest<IReadOnlyList<DocumentProcessingLogDto>>;
