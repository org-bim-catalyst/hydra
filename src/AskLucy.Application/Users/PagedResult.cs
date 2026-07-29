namespace AskLucy.Application.Users;

/// <summary>Offset pagination — acceptable for "small stable admin lists" per constitution &#167;6 at this feature's inherited &lt;100-user scale.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
