using MediatR;

namespace AskLucy.Application.Documents.Queries.GetVersionTimeline;

public sealed record GetVersionTimelineQuery(Guid DocumentId) : IRequest<IReadOnlyList<DocumentVersionSummaryDto>>;
