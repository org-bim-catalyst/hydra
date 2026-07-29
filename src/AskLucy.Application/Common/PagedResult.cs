namespace AskLucy.Application.Common;

/// <summary>
/// A cursor-paginated result page (constitution &#167;6 — cursor-based pagination for
/// high-churn collections; research.md Topic 6). <see cref="NextCursor"/> is an opaque
/// token the caller passes back to fetch the next page; null means there is no next page.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
