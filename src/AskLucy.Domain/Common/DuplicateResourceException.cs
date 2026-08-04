namespace AskLucy.Domain.Common;

/// <summary>
/// Thrown when an operation would create a resource that collides with an existing one under
/// a uniqueness rule scoped to the caller (e.g. a custom category name already used by the
/// same owner) — maps to 409, distinct from <see cref="DomainRuleViolationException"/>'s 400
/// (constitution &#167;4 Error handling; not reused via <c>DbUpdateConcurrencyException</c>,
/// whose 409 means something different — a stale RowVersion, not a name collision).
/// </summary>
public sealed class DuplicateResourceException(string message) : Exception(message);
