namespace AskLucy.Domain.Common;

/// <summary>
/// A write was rejected because the resource had already changed since the caller last read it
/// (optimistic concurrency). Distinct from <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// (an Infrastructure/EF Core type Application/Domain must never reference, constitution §3) —
/// this is the Application-layer-safe equivalent, mapped to the same 409 Problem Details.
/// </summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);
