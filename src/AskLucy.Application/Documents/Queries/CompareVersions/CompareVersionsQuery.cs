using MediatR;

namespace AskLucy.Application.Documents.Queries.CompareVersions;

public sealed record CompareVersionsQuery(Guid DocumentId, Guid FromVersionId, Guid ToVersionId) : IRequest<DocumentVersionCompareDto>;
