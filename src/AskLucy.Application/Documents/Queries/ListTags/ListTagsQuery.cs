using MediatR;

namespace AskLucy.Application.Documents.Queries.ListTags;

/// <summary>contracts/documents-api.md `GET /api/v1/documents/tags` — the caller's own tags, for filter/autocomplete UI (FR-032).</summary>
public sealed record ListTagsQuery : IRequest<IReadOnlyList<string>>;
