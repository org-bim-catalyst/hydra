namespace AskLucy.Application.Abstractions;

/// <summary>
/// A single business transaction commits through exactly one <see cref="SaveChangesAsync"/>
/// call, per constitution &#167;3 (Repository &amp; Unit of Work rules).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
