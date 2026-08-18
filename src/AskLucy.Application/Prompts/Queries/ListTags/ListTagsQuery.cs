using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListTags;

/// <summary>Distinct tag values the caller has used across their own prompts (spec.md FR-052) — populates tag-filter autocomplete.</summary>
public sealed record ListTagsQuery : IRequest<IReadOnlyList<string>>;
