using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListTags;

/// <summary>Distinct tag values the caller has used, optionally prefix-filtered (FR-020).</summary>
public sealed record ListTagsQuery(string? Prefix = null) : IRequest<IReadOnlyList<string>>;
